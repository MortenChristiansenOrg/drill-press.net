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
    private readonly ExpressionSyntax _replacement;
    private readonly ExpressionInput[] _inputs;
    private readonly Func<RewriteEvidence, ProofResult>[] _checks;

    internal ExpressionReplacement(
        AnalysisSource source,
        SyntaxNode original,
        ExpressionSyntax replacement,
        ExpressionInput[] inputs,
        Func<RewriteEvidence, ProofResult>[]? checks = null
    )
    {
        _source = source;
        _original = original;
        _replacement = replacement.WithoutTrivia();
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
                    change =>
                        change.BeforeModel.GetOperation(change.Before)
                            is IUnaryOperation
                            {
                                OperatorMethod: null,
                                Type.SpecialType: SpecialType.System_Boolean,
                            }
                            ? ProofResult.Proven
                            : ProofResult.Unknown,
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
    public FixProposal? SafeWhen(Func<ExpressionChange, bool> proof)
    {
        if (
            _original is not ExpressionSyntax original
            || _original.SyntaxTree != _source.Tree
            || !_source.Document.IsEditable
            || _source.Document.IsGenerated
            || ContextualRewrite.HasInteriorContent(_original)
            || ContextualRewrite.IsObservableSyntax(_source, _original)
            || _inputs.Any(input =>
                input.Source != _source || !_original.Span.Contains(input.Original.Span)
            )
        )
            return null;
        var replacement = ExpressionLayout.Fit(_source, original, _replacement);
        var text = replacement.ToString();
        if (_original.ToString() == text)
            return null;
        var edit = SourceChanges.Replace(_source, _original.Span, text);
        return SourceChanges.Propose([edit], context => Validate(context, replacement, proof));
    }

    private bool Validate(
        RewriteContext context,
        ExpressionSyntax replacement,
        Func<ExpressionChange, bool> proof
    )
    {
        var memberships = context
            .Original.Sources.Where(source =>
                source.Document.FileIdentity == _source.Document.FileIdentity
            )
            .ToArray();
        return memberships.Length > 0
            && memberships.All(source => ValidateMembership(context, source, replacement, proof));
    }

    private bool ValidateMembership(
        RewriteContext context,
        AnalysisSource source,
        ExpressionSyntax replacement,
        Func<ExpressionChange, bool> proof
    )
    {
        var before = source
            .Tree.GetRoot(source.Project.CancellationToken)
            .FindNode(_original.Span, getInnermostNodeForTie: true);
        if (
            before.Span != _original.Span
            || before.RawKind != _original.RawKind
            || ContextualRewrite.HasInteriorContent(before)
            || ContextualRewrite.IsObservableSyntax(source, before)
            || context.Map(source, before) is not { After: ExpressionSyntax } mapped
            || context.RewrittenSource(source) is not { } rewritten
        )
            return false;
        var inputs = ReadInputs(source, mapped, replacement);
        if (inputs is null)
            return false;
        var evidence = new RewriteEvidence(context, mapped, inputs);
        return RewriteChecks.SameEnclosingBindings(evidence) == ProofResult.Proven
            && RewriteChecks.SameRetainedBindings(evidence) == ProofResult.Proven
            && RewriteChecks.SameCompilerSuppliedArguments(evidence) == ProofResult.Proven
            && _checks.All(check => check(evidence) == ProofResult.Proven)
            && proof(new ExpressionChange(evidence, rewritten));
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
