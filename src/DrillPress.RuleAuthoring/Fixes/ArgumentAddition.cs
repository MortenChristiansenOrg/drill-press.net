using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

/// <summary>An immutable argument addition: supplies an omitted optional argument of the same overload, or moves the call to an expected overload with one more parameter. The added value's meaning is always a rule obligation.</summary>
/// <remarks>Built-in gates: editable ordinary source without interior comments, outside nameof and expression trees; a resolved call without expanded params; and, in every affected compilation, the expected overload, the new argument bound to the named parameter, unchanged retained arguments, receiver, conversions, enclosing bindings and other compiler-supplied arguments.</remarks>
public sealed class ArgumentAddition
{
    private readonly AnalysisSource _source;
    private readonly SyntaxNode _syntax;
    private readonly string _parameter;
    private readonly string _expression;
    private readonly MethodTransition? _transition;

    internal ArgumentAddition(
        AnalysisSource source,
        SyntaxNode syntax,
        string parameter,
        string expression,
        MethodTransition? transition = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        if (SyntaxFactory.ParseExpression(expression).ContainsDiagnostics)
            throw new ArgumentException(
                "The value must be a complete C# expression.",
                nameof(expression)
            );
        _source = source;
        _syntax = syntax;
        _parameter = parameter;
        _expression = expression;
        _transition = transition;
    }

    /// <summary>Requires the call to move from one exact overload to another that has the added parameter, keeping its constructed type arguments; every other parameter keeps its name and type.</summary>
    public ArgumentAddition ExpectOverloadChange(CodeMember from, CodeMember to) =>
        ExpectTransition(new MethodTransition(from, to));

    /// <summary>Requires an explicitly resolved overload pair, for transitions that descriptors cannot express.</summary>
    public ArgumentAddition ExpectTransition(MethodTransition transition) =>
        new(_source, _syntax, _parameter, _expression, transition);

    /// <summary>Proposes the addition when your proof of the added value's meaning holds in every affected compilation; false withholds the fix and keeps the finding.</summary>
    /// <param name="proof">States why the new argument is right, such as <c>change =&gt; change.AddedValue.RefersTo(ordinal)</c>.</param>
    public FixProposal? SafeWhen(Func<ArgumentAdditionChange, bool> proof)
    {
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
        if (Read(_source, syntax) is not { } call)
            return FixProposal.Withheld("the call does not resolve");
        if (
            call.Arguments.Any(argument =>
                argument.Kind is ArgumentKind.ParamArray or ArgumentKind.ParamCollection
            )
        )
            return FixProposal.Withheld("the call expands a params argument");
        if (
            call.Arguments.Any(argument =>
                argument.IsExplicit && argument.Parameter.Name == _parameter
            )
        )
            return FixProposal.Withheld($"the call already passes {_parameter}");
        if (_transition is null && call.Parameter(_parameter) is null)
            return FixProposal.Withheld(
                $"{call.Target.Name} has no {_parameter} parameter; declare the overload change with ExpectOverloadChange"
            );
        var arguments = syntax.ArgumentList.Arguments;
        var positional =
            arguments.All(argument => argument.NameColon is null)
            && Ordinal(call) == arguments.Count;
        var text =
            (arguments.Count > 0 ? ", " : "") + (positional ? "" : _parameter + ": ") + _expression;
        var edit = SourceChanges.Replace(
            _source,
            new(syntax.ArgumentList.CloseParenToken.SpanStart, 0),
            text
        );
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

    // The added parameter's position in the destination overload, when it is known before the edit.
    private int Ordinal(CodeInvocation call) =>
        _transition is null
            ? call.Declaration.Parameters.FirstOrDefault(parameter =>
                parameter.Name == _parameter
            )?.Ordinal
                ?? -1
            : _transition
                .Resolve(call.Source.Project, call.Declaration)
                ?.After.Parameters.FirstOrDefault(parameter => parameter.Name == _parameter)
                ?.Ordinal
                ?? -1;

    private string? Validate(
        RewriteContext context,
        AnalysisSource source,
        Func<ArgumentAdditionChange, bool> proof,
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
            || ContextualRewrite.IsObservableSyntax(source, syntax)
        )
            return "the call differs in this compilation";
        if (
            context.Evidence(source, syntax, edits)
                is not { After: InvocationExpressionSyntax afterSyntax } rewrite
            || context.RewrittenSource(source) is not { } rewrittenSource
            || rewrite.AfterModel.GetOperation(afterSyntax) is not IInvocationOperation operation
        )
            return "the call has no counterpart after the edit";
        var after = new CodeInvocation(rewrittenSource, operation);
        MethodPair? expected = null;
        if (_transition is null)
        {
            if (!context.Same(before.Declaration, after.Declaration))
                return $"the edited call binds to {after.Target.ToDisplayString()} instead of the same overload";
        }
        else if (
            (expected = _transition.Resolve(source.Project, before.Declaration)) is null
            || !SymbolEqualityComparer.Default.Equals(
                before.Declaration,
                ArgumentRemoval.Normalize(expected.Before)
            )
        )
            return "the call does not bind to the expected original overload";
        else if (
            !context.Same(ArgumentRemoval.Normalize(expected.After), after.Declaration)
            || !ArgumentTransitionChecks.SameTypeArguments(
                before.Declaration,
                after.Declaration,
                context
            )
        )
            return $"the edited call binds to {after.Target.ToDisplayString()}, not the expected overload";
        if (
            after.Arguments.SingleOrDefault(argument =>
                argument.IsExplicit && argument.Parameter.Name == _parameter
            )
            is not { Value: { } added }
        )
            return $"the new argument does not bind to {_parameter}";
        if (Retained(rewrite, before, after) is { } retained)
            return retained;
        if (RewriteChecks.EnclosingBindings(rewrite) is { Holds: false } enclosing)
            return enclosing.Reason;
        if (RewriteChecks.CompilerSuppliedArguments(rewrite, syntax) is { Holds: false } supplied)
            return supplied.Reason;
        return proof(new ArgumentAdditionChange(rewrite, before, after, added, expected))
            ? null
            : "the rule's SafeWhen proof returned false";
    }

    // Every original explicit argument keeps its order, parameter name, kind and binding, the receiver is
    // unchanged, and compiler-supplied values other than the added parameter stay the same.
    private string? Retained(RewriteEvidence rewrite, CodeInvocation before, CodeInvocation after)
    {
        var oldValues = before
            .Arguments.Where(argument => argument.IsExplicit && !argument.IsReceiver)
            .ToArray();
        var newValues = after
            .Arguments.Where(argument =>
                argument.IsExplicit && !argument.IsReceiver && argument.Parameter.Name != _parameter
            )
            .ToArray();
        if (
            before.IsConditional != after.IsConditional
            || before.Declaration.IsStatic != after.Declaration.IsStatic
            || oldValues.Length != newValues.Length
            || oldValues
                .Zip(newValues)
                .Any(pair =>
                    pair.First.Parameter.Name != pair.Second.Parameter.Name
                    || pair.First.Kind != pair.Second.Kind
                    || pair.First.RefKind != pair.Second.RefKind
                    || pair.First.Value?.Syntax is not { } oldSyntax
                    || pair.Second.Value?.Syntax is not { } newSyntax
                    || !ArgumentTransitionChecks.SameInput(rewrite, oldSyntax, newSyntax)
                )
        )
            return "a retained argument changes order, parameter, conversion or binding";
        if (before.Receiver is { IsImplicit: false } oldReceiver)
        {
            if (
                after.Receiver is not { IsImplicit: false } newReceiver
                || !ArgumentTransitionChecks.SameInput(
                    rewrite,
                    oldReceiver.Syntax,
                    newReceiver.Syntax
                )
            )
                return "the receiver changes";
        }
        else if (after.Receiver is { IsImplicit: false })
            return "the receiver changes";
        var oldDefaults = Defaults(before.Operation, _parameter);
        var newDefaults = Defaults(after.Operation, _parameter);
        return oldDefaults.SequenceEqual(newDefaults)
            ? null
            : "another compiler-supplied argument of the call changes";
    }

    private static IEnumerable<(string?, object?)> Defaults(
        IInvocationOperation call,
        string added
    ) =>
        call
            .Arguments.Where(argument =>
                argument.ArgumentKind == ArgumentKind.DefaultValue
                && argument.Parameter?.Name != added
            )
            .Select(argument =>
                (
                    argument.Parameter?.Name,
                    argument.Value.ConstantValue.HasValue
                        ? argument.Value.ConstantValue.Value
                        : null
                )
            );

    private static CodeInvocation? Read(AnalysisSource source, InvocationExpressionSyntax syntax) =>
        source.Model.GetOperation(syntax) is IInvocationOperation operation
        && new CodeInvocation(source, operation) is { IsResolved: true } call
            ? call
            : null;
}
