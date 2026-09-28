using DrillPress.Operations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress.Fixes;

/// <summary>An immutable bound argument-removal plan requiring separate value, evaluation-loss and overload-behavior proofs.</summary>
public sealed class ArgumentRemoval
{
    private readonly AnalysisSource _source;
    private readonly SyntaxNode _syntax;
    private readonly string _parameter;
    private readonly Func<RewriteEvidence, ProofResult>[] _contextChecks;
    private readonly MethodTransition? _transition;
    private readonly Func<ArgumentRemovalEvidence, ProofResult>? _value;
    private readonly Func<ArgumentRemovalEvidence, ProofResult>? _evaluation;
    private readonly Func<ArgumentRemovalEvidence, ProofResult>? _defaults;

    internal ArgumentRemoval(
        AnalysisSource source,
        SyntaxNode syntax,
        string parameter,
        Func<RewriteEvidence, ProofResult>[] checks,
        MethodTransition? transition = null,
        Func<ArgumentRemovalEvidence, ProofResult>? value = null,
        Func<ArgumentRemovalEvidence, ProofResult>? evaluation = null,
        Func<ArgumentRemovalEvidence, ProofResult>? defaults = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
        _source = source;
        _syntax = syntax;
        _parameter = parameter;
        _contextChecks = checks;
        _transition = transition;
        _value = value;
        _evaluation = evaluation;
        _defaults = defaults;
    }

    /// <summary>Requires the actual before/after overloads to match this exact contextual pair and parameter map.</summary>
    public ArgumentRemoval RequireTransition(MethodTransition transition) =>
        new(
            _source,
            _syntax,
            _parameter,
            _contextChecks,
            transition,
            _value,
            _evaluation,
            _defaults
        );

    /// <summary>Requires proof that the removed value denotes the configured semantic default; this does not prove that losing evaluation is harmless.</summary>
    public ArgumentRemoval RequireRemovedValue(Func<ArgumentRemovalEvidence, ProofResult> proof) =>
        new(
            _source,
            _syntax,
            _parameter,
            _contextChecks,
            _transition,
            proof,
            _evaluation,
            _defaults
        );

    /// <summary>Requires separate approval for losing the exact getter/value/conversion evaluation, including initialization, side effects and exceptions.</summary>
    public ArgumentRemoval RequireRemovedEvaluation(
        Func<ArgumentRemovalEvidence, ProofResult> proof
    ) => new(_source, _syntax, _parameter, _contextChecks, _transition, _value, proof, _defaults);

    /// <summary>Explicitly approves changed synthesized arguments at this one verified call. Other calls' compiler-supplied values remain protected.</summary>
    public ArgumentRemoval RequireSynthesizedArguments(
        Func<ArgumentRemovalEvidence, ProofResult> proof
    ) => new(_source, _syntax, _parameter, _contextChecks, _transition, _value, _evaluation, proof);

    /// <summary>Adds an invariant to the default retained-argument and enclosing-binding checks.</summary>
    public ArgumentRemoval Require(Func<RewriteEvidence, ProofResult> check) =>
        new(
            _source,
            _syntax,
            _parameter,
            [.. _contextChecks, check],
            _transition,
            _value,
            _evaluation,
            _defaults
        );

    /// <summary>Creates an atomic edit only with all three required proofs. Expanded params, receivers, absent arguments and ambiguous trivia are not removable.</summary>
    public FixProposal? Propose(Func<ArgumentRemovalEvidence, ProofResult> provesOverloadBehavior)
    {
        if (
            _transition is null
            || _value is null
            || _evaluation is null
            || _syntax is not InvocationExpressionSyntax syntax
            || syntax.SyntaxTree != _source.Tree
            || !_source.Document.IsEditable
            || _source.Document.IsGenerated
            || ContextualRewrite.IsObservableSyntax(_source, syntax)
            || ContextualRewrite.HasInteriorContent(syntax.ArgumentList)
            || Read(_source, syntax) is not { } original
            || Removable(original) is not { SourceIndex: { } index }
        )
            return null;
        var replacement = syntax.ArgumentList.RemoveNode(
            syntax.ArgumentList.Arguments[index],
            SyntaxRemoveOptions.KeepNoTrivia
        );
        if (replacement is null)
            return null;
        var edit = SourceChanges.Replace(_source, syntax.ArgumentList.Span, replacement.ToString());
        return SourceChanges.Propose(
            [edit],
            context =>
            {
                var sources = context
                    .Original.Sources.Where(source =>
                        source.Document.FileIdentity == _source.Document.FileIdentity
                    )
                    .ToArray();
                return sources.Length > 0
                    && sources.All(source => Validate(context, source, provesOverloadBehavior));
            }
        );
    }

    private bool Validate(
        RewriteContext context,
        AnalysisSource source,
        Func<ArgumentRemovalEvidence, ProofResult> provesBehavior
    )
    {
        var syntax =
            source
                .Tree.GetRoot(source.Project.CancellationToken)
                .FindNode(_syntax.Span, getInnermostNodeForTie: true) as InvocationExpressionSyntax;
        if (
            syntax is null
            || syntax.Span != _syntax.Span
            || Read(source, syntax) is not { } before
            || Removable(before) is not { } removed
            || ContextualRewrite.IsObservableSyntax(source, syntax)
            || context.Evidence(source, syntax)
                is not { After: InvocationExpressionSyntax afterSyntax } rewrite
            || rewrite.AfterModel.GetOperation(afterSyntax) is not IInvocationOperation after
            || _transition!.Resolve(source.Project) is not { } expected
            || !SymbolEqualityComparer.Default.Equals(
                before.Declaration,
                Normalize(expected.Before)
            )
            || !RewriteSymbols.Same(
                Normalize(expected.After),
                Normalize(after.TargetMethod),
                context
            )
            || !ArgumentTransitionChecks.SameTypeArguments(
                before.Declaration,
                Normalize(after.TargetMethod),
                context
            )
        )
            return false;
        var evidence = new ArgumentRemovalEvidence(
            rewrite,
            before.Operation,
            after,
            removed,
            expected
        );
        return ArgumentTransitionChecks.RetainedArguments(evidence, _transition.Parameters)
            && RewriteChecks.SameEnclosingBindings(rewrite) == ProofResult.Proven
            && RewriteChecks.CompilerSuppliedArguments(rewrite, syntax) == ProofResult.Proven
            && (
                ArgumentTransitionChecks.SameDefaults(evidence, _transition.Parameters)
                || _defaults?.Invoke(evidence) == ProofResult.Proven
            )
            && _contextChecks.All(check => check(rewrite) == ProofResult.Proven)
            && _value!(evidence) == ProofResult.Proven
            && _evaluation!(evidence) == ProofResult.Proven
            && provesBehavior(evidence) == ProofResult.Proven;
    }

    private CodeArgument? Removable(CodeInvocation call) =>
        call.Parameter(_parameter)?.Values is { Count: 1 } values
        && values[0]
            is {
                Kind: ArgumentKind.Explicit,
                IsReceiver: false,
                SourceIndex: not null,
                Parameter.IsParams: false
            } value
            ? value
            : null;

    private static CodeInvocation? Read(AnalysisSource source, InvocationExpressionSyntax syntax) =>
        source.Model.GetOperation(syntax) is IInvocationOperation operation
        && new CodeInvocation(source, operation) is { IsResolved: true } call
            ? call
            : null;

    internal static IMethodSymbol Normalize(IMethodSymbol method) =>
        method.ReducedFrom is { } reduced
            ? method.Arity == 0
                ? reduced
                : reduced.Construct(method.TypeArguments.ToArray())
            : method;
}
