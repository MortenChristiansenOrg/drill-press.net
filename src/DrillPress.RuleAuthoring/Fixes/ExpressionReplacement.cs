using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

/// <summary>An immutable expression replacement awaiting its behavior proof.</summary>
/// <remarks>Built-in gates: editable ordinary source; no interior comments, directives or disabled text; not inside nameof or an expression tree; every affected compilation compiles and keeps the enclosing expressions' bindings, kept operands' bindings and compiler-supplied arguments. Evaluation count and order, receiver null behavior and the meaning of the new expression are separate obligations.</remarks>
public sealed class ExpressionReplacement
{
    private readonly AnalysisSource _source;
    private readonly SyntaxNode _original;
    private readonly ExpressionSyntax? _replacement;
    private readonly ExpressionInput[] _inputs;
    private readonly (string Name, Func<RewriteEvidence, ProofResult> Prove)[] _checks;

    internal ExpressionReplacement(
        AnalysisSource source,
        SyntaxNode original,
        ExpressionSyntax? replacement,
        ExpressionInput[] inputs,
        (string Name, Func<RewriteEvidence, ProofResult> Prove)[]? checks = null
    )
    {
        _source = source;
        _original = original;
        _replacement = replacement?.WithoutTrivia();
        _inputs = inputs;
        _checks = checks ?? [];
    }

    internal static ExpressionReplacement Equality(
        AnalysisSource source,
        ExpressionSyntax target,
        CodeExpression left,
        CodeExpression right,
        bool absorbNegation
    )
    {
        var inputs = new[]
        {
            new ExpressionInput(left.Source, left.Syntax),
            new ExpressionInput(right.Source, right.Syntax),
        };
        SyntaxNode replaced = target;
        while (replaced.Parent is ParenthesizedExpressionSyntax parentheses)
            replaced = parentheses;
        if (
            absorbNegation
            && replaced.Parent is PrefixUnaryExpressionSyntax negation
            && negation.IsKind(SyntaxKind.LogicalNotExpression)
            && source.Model.GetOperation(negation, source.Project.CancellationToken)
                is IUnaryOperation
                {
                    OperatorMethod: null,
                    Type.SpecialType: SpecialType.System_Boolean
                }
        )
            return new(
                source,
                negation,
                ExpressionTemplates.Create("{0} != {1}", inputs).Syntax,
                inputs,
                [
                    (
                        "absorbed negation",
                        change =>
                            change.BeforeModel.GetOperation(change.Before)
                                is IUnaryOperation
                                {
                                    OperatorMethod: null,
                                    Type.SpecialType: SpecialType.System_Boolean,
                                }
                                ? ProofResult.Proven
                                : ProofResult.Unknown
                    ),
                ]
            );
        return new(source, target, ExpressionTemplates.Create("{0} == {1}", inputs).Syntax, inputs);
    }

    /// <summary>Adds bounded behavior checks for kept operands, such as unchanged evaluation count and order. These checks never prove that the new expression means the same thing.</summary>
    public ExpressionReplacement MustPreserve(ExpressionBehavior behavior) =>
        new(
            _source,
            _original,
            _replacement,
            _inputs,
            [.. _checks, .. BehaviorChecks.Expressions(behavior)]
        );

    /// <summary>Proposes the replacement when your proof holds in every affected compilation; false withholds the fix and keeps the finding.</summary>
    /// <param name="proof">States why the change preserves behavior, using the original and rewritten expressions, such as <c>change =&gt; change.After.TypeIs&lt;bool&gt;()</c>.</param>
    /// <returns>The proposal, or a <see cref="FixProposal.Withheld"/> proposal naming the source shape that prevents it.</returns>
    public FixProposal? SafeWhen(Func<ExpressionChange, bool> proof)
    {
        if (Ineligible() is { } reason)
            return FixProposal.Withheld(reason);
        if (_replacement is null)
            return FixProposal.Withheld("no spelling of the referenced symbol binds to it here");
        var original = (ExpressionSyntax)_original;
        var replacement = ExpressionLayout.Fit(_source, original, _replacement);
        var text = replacement.ToString();
        if (_original.ToString() == text)
            return FixProposal.Withheld("the replacement is identical to the original expression");
        var edit = SourceChanges.Replace(_source, _original.Span, text);
        return SourceChanges.ProposeExplained(
            [edit],
            context => Validate(context, replacement, proof, [edit])
        );
    }

    private string? Ineligible() =>
        _original is not ExpressionSyntax ? "the replaced node is not an expression"
        : _original.SyntaxTree != _source.Tree
            ? "the replaced expression belongs to another compilation membership"
        : !_source.Document.IsEditable || _source.Document.IsGenerated
            ? "the expression is in generated or non-editable source"
        : ContextualRewrite.HasInteriorContent(_original)
            ? "the expression contains comments, directives or disabled text"
        : ContextualRewrite.IsObservableSyntax(_source, _original)
            ? "the expression is inside nameof or an expression tree"
        : _inputs.Any(input =>
            input.Source != _source || !_original.Span.Contains(input.Original.Span)
        )
            ? "a kept operand is not part of the replaced expression"
        : null;

    private string? Validate(
        RewriteContext context,
        ExpressionSyntax replacement,
        Func<ExpressionChange, bool> proof,
        IReadOnlyList<SourceEdit> edits
    )
    {
        var memberships = context
            .Original.Sources.Where(source =>
                source.Document.FileIdentity == _source.Document.FileIdentity
            )
            .ToArray();
        if (memberships.Length == 0)
            return "the edited file is not part of this compilation";
        foreach (var source in memberships)
            if (ValidateMembership(context, source, replacement, proof, edits) is { } reason)
                return reason;
        return null;
    }

    private string? ValidateMembership(
        RewriteContext context,
        AnalysisSource source,
        ExpressionSyntax replacement,
        Func<ExpressionChange, bool> proof,
        IReadOnlyList<SourceEdit> edits
    )
    {
        var before = source
            .Tree.GetRoot(source.Project.CancellationToken)
            .FindNode(_original.Span, getInnermostNodeForTie: true);
        if (before.Span != _original.Span || before.RawKind != _original.RawKind)
            return "the expression parses differently in this compilation";
        if (
            ContextualRewrite.HasInteriorContent(before)
            || ContextualRewrite.IsObservableSyntax(source, before)
        )
            return "the expression contains comments or is inside nameof or an expression tree in this compilation";
        if (
            context.Map(source, before) is not { After: ExpressionSyntax } mapped
            || context.RewrittenSource(source) is not { } rewritten
        )
            return "the replacement does not parse as one expression in place of the original";
        var inputs = ReadInputs(source, mapped, replacement);
        if (inputs is null)
            return "a kept operand cannot be found in the replacement";
        var evidence = new RewriteEvidence(context, mapped, inputs, edits);
        if (RewriteChecks.EnclosingBindings(evidence) is { Holds: false } enclosing)
            return enclosing.Reason;
        if (RewriteChecks.SameRetainedBindings(evidence) != ProofResult.Proven)
            return "a kept operand changes syntax, type or binding";
        if (RewriteChecks.CompilerSuppliedArguments(evidence, null) is { Holds: false } supplied)
            return supplied.Reason;
        foreach (var (name, prove) in _checks)
            if (prove(evidence) is var result && result != ProofResult.Proven)
                return $"the {name} check is {result.ToString().ToLowerInvariant()}";
        return proof(new ExpressionChange(evidence, rewritten))
            ? null
            : "the rule's SafeWhen proof returned false";
    }

    private IReadOnlyList<InputRewrite>? ReadInputs(
        AnalysisSource source,
        NodeRewrite target,
        ExpressionSyntax replacement
    )
    {
        var inputs = new List<InputRewrite>();
        foreach (var input in _inputs)
        {
            var before =
                source
                    .Tree.GetRoot(source.Project.CancellationToken)
                    .FindNode(input.Original.Span, getInnermostNodeForTie: true)
                as ExpressionSyntax;
            if (before is null || before.Span != input.Original.Span)
                return null;
            var after = new List<ExpressionSyntax>();
            foreach (var marker in replacement.GetAnnotatedNodes(input.Annotation))
            {
                var span = new TextSpan(
                    target.After.SpanStart + marker.SpanStart - replacement.SpanStart,
                    marker.Span.Length
                );
                var node = target
                    .After.SyntaxTree.GetRoot(source.Project.CancellationToken)
                    .FindNode(span, getInnermostNodeForTie: true);
                if (node is not ExpressionSyntax expression || expression.Span != span)
                    return null;
                after.Add(expression);
            }
            inputs.Add(new(before, after.AsReadOnly()));
        }
        return inputs.AsReadOnly();
    }
}
