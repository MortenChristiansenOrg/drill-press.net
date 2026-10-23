using DrillPress;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

/// <summary>An immutable argument removal. Removing an argument changes the selected overload, so the plan needs the expected overload change plus separate proofs that the removed value and its evaluation do not matter.</summary>
/// <remarks>Built-in gates: editable ordinary source without interior comments, outside nameof and expression trees; one explicit, non-params, non-receiver argument; and, in every affected compilation, the expected constructed overload change, unchanged remaining arguments, conversions, evaluation order, enclosing bindings and compiler-supplied arguments.</remarks>
public sealed class ArgumentRemoval
{
    private readonly AnalysisSource _source;
    private readonly SyntaxNode _syntax;
    private readonly string _parameter;
    private readonly MethodTransition? _transition;
    private readonly Func<ArgumentRemovalChange, bool>? _value;
    private readonly Func<ArgumentRemovalChange, bool>? _evaluation;
    private readonly Func<ArgumentRemovalChange, bool>? _defaults;

    internal ArgumentRemoval(
        AnalysisSource source,
        SyntaxNode syntax,
        string parameter,
        MethodTransition? transition = null,
        Func<ArgumentRemovalChange, bool>? value = null,
        Func<ArgumentRemovalChange, bool>? evaluation = null,
        Func<ArgumentRemovalChange, bool>? defaults = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
        _source = source;
        _syntax = syntax;
        _parameter = parameter;
        _transition = transition;
        _value = value;
        _evaluation = evaluation;
        _defaults = defaults;
    }

    /// <summary>Requires the call to move from one exact overload to another, keeping its constructed type arguments; parameters with equal names and types correspond.</summary>
    public ArgumentRemoval ExpectOverloadChange(CodeMember from, CodeMember to) =>
        ExpectTransition(new MethodTransition(from, to));

    /// <summary>Requires an explicitly configured overload pair and parameter map, for transitions that descriptors cannot express.</summary>
    public ArgumentRemoval ExpectTransition(MethodTransition transition) =>
        new(_source, _syntax, _parameter, transition, _value, _evaluation, _defaults);

    /// <summary>Proves that the removed value equals what the new overload uses, such as <c>change =&gt; change.RemovedValue?.RefersTo(ordinal) == true</c>. This does not approve losing its evaluation.</summary>
    public ArgumentRemoval RequireRemovedValue(Func<ArgumentRemovalChange, bool> proof) =>
        new(_source, _syntax, _parameter, _transition, proof, _evaluation, _defaults);

    /// <summary>Separately approves no longer evaluating the removed expression, including getters, conversions, initialization, side effects and exceptions.</summary>
    public ArgumentRemoval RequireRemovedEvaluation(Func<ArgumentRemovalChange, bool> proof) =>
        new(_source, _syntax, _parameter, _transition, _value, proof, _defaults);

    /// <summary>Approves compiler-supplied optional arguments that change at this call. Other calls' compiler-supplied values remain protected.</summary>
    public ArgumentRemoval RequireSynthesizedArguments(Func<ArgumentRemovalChange, bool> proof) =>
        new(_source, _syntax, _parameter, _transition, _value, _evaluation, proof);

    /// <summary>Proposes the removal when the overload change is expected, the value and evaluation proofs hold, and your final proof that both overloads behave the same holds in every affected compilation.</summary>
    public FixProposal? SafeWhen(Func<ArgumentRemovalChange, bool> proof)
    {
        if (_transition is null)
            return FixProposal.Withheld("the removal has no expected overload change");
        if (_value is null || _evaluation is null)
            return FixProposal.Withheld(
                "the removal needs both RequireRemovedValue and RequireRemovedEvaluation"
            );
        if (
            _syntax is not InvocationExpressionSyntax syntax
            || syntax.SyntaxTree != _source.Tree
            || !_source.Document.IsEditable
            || _source.Document.IsGenerated
        )
            return FixProposal.Withheld(
                "the call is not an invocation in editable ordinary source"
            );
        if (
            ContextualRewrite.IsObservableSyntax(_source, syntax)
            || ContextualRewrite.HasInteriorContent(syntax.ArgumentList)
        )
            return FixProposal.Withheld(
                "the call is inside nameof or an expression tree, or its arguments contain comments"
            );
        if (Read(_source, syntax) is not { } original)
            return FixProposal.Withheld("the call does not resolve");
        if (Removable(original) is not { SourceIndex: { } index })
            return FixProposal.Withheld(
                $"no single explicit, non-params, non-receiver argument binds to {_parameter}"
            );
        var replacement = syntax.ArgumentList.RemoveNode(
            syntax.ArgumentList.Arguments[index],
            SyntaxRemoveOptions.KeepNoTrivia
        );
        if (replacement is null)
            return FixProposal.Withheld("the argument cannot be removed from the list");
        var edit = SourceChanges.Replace(_source, syntax.ArgumentList.Span, replacement.ToString());
        return SourceChanges.ProposeExplained(
            [edit],
            context =>
            {
                var sources = context
                    .Original.Sources.Where(source =>
                        source.Document.FileIdentity == _source.Document.FileIdentity
                    )
                    .ToArray();
                if (sources.Length == 0)
                    return "the edited file is not part of this compilation";
                foreach (var source in sources)
                    if (Validate(context, source, proof, [edit]) is { } reason)
                        return reason;
                return null;
            }
        );
    }

    private string? Validate(
        RewriteContext context,
        AnalysisSource source,
        Func<ArgumentRemovalChange, bool> provesBehavior,
        IReadOnlyList<SourceEdit> edits
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
        )
            return "the call or argument differs in this compilation";
        if (
            context.Evidence(source, syntax, edits)
                is not { After: InvocationExpressionSyntax afterSyntax } rewrite
            || rewrite.AfterModel.GetOperation(afterSyntax) is not IInvocationOperation after
        )
            return "the call has no counterpart after the edit";
        if (
            _transition!.Resolve(source.Project, before.Declaration) is not { } expected
            || !SymbolEqualityComparer.Default.Equals(
                before.Declaration,
                Normalize(expected.Before)
            )
        )
            return "the call does not bind to the expected original overload";
        if (
            !RewriteSymbols.Same(Normalize(expected.After), Normalize(after.TargetMethod), context)
            || !ArgumentTransitionChecks.SameTypeArguments(
                before.Declaration,
                Normalize(after.TargetMethod),
                context
            )
        )
            return $"the edited call binds to {after.TargetMethod.ToDisplayString()}, not the expected overload";
        var evidence = new ArgumentRemovalChange(
            rewrite,
            before.Operation,
            after,
            removed,
            expected
        );
        var parameters = _transition.ParametersFor(expected);
        if (parameters is null)
            return "the overloads' parameters cannot be mapped by name and type";
        if (!ArgumentTransitionChecks.RetainedArguments(evidence, parameters))
            return "a retained argument changes order, parameter, conversion or binding";
        if (RewriteChecks.EnclosingBindings(rewrite) is { Holds: false } enclosing)
            return enclosing.Reason;
        if (RewriteChecks.CompilerSuppliedArguments(rewrite, syntax) is { Holds: false } supplied)
            return supplied.Reason;
        if (
            !ArgumentTransitionChecks.SameDefaults(evidence, parameters)
            && _defaults?.Invoke(evidence) != true
        )
            return "compiler-supplied arguments change without RequireSynthesizedArguments approval";
        if (!_value!(evidence))
            return "the RequireRemovedValue proof returned false";
        if (!_evaluation!(evidence))
            return "the RequireRemovedEvaluation proof returned false";
        return provesBehavior(evidence) ? null : "the rule's SafeWhen proof returned false";
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
