using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress.Fixes;

/// <summary>An immutable expression-edit plan with default source/trivia/context gates and additive contextual proofs.</summary>
public sealed class FixBuilder
{
    private readonly AnalysisSource _source;
    private readonly SyntaxNode _original;
    private readonly ExpressionSyntax? _replacement;
    private readonly ExpressionInput[] _inputs;
    private readonly Func<RewriteEvidence, ProofResult>[] _checks;

    internal FixBuilder(
        AnalysisSource source,
        SyntaxNode original,
        ExpressionSyntax? replacement = null,
        ExpressionInput[]? inputs = null,
        Func<RewriteEvidence, ProofResult>[]? checks = null
    )
    {
        _source = source;
        _original = original;
        _replacement = replacement;
        _inputs = inputs ?? [];
        _checks = checks ?? [];
    }

    /// <summary>Replaces an expression, adding parentheses for compound replacement syntax and retaining exterior trivia.</summary>
    public FixBuilder ReplaceWith(ExpressionSyntax replacement) =>
        new(_source, _original, Prepare(replacement), _inputs, _checks);

    /// <summary>Registers annotated retained inputs. Mappings are verified after reparsing in every context.</summary>
    public FixBuilder MapInputs(params ExpressionInput[] inputs) =>
        new(_source, _original, _replacement, inputs.ToArray(), _checks);

    /// <summary>Adds an invariant; unknown or disproven evidence withholds the complete proposal.</summary>
    public FixBuilder Require(Func<RewriteEvidence, ProofResult> check) =>
        new(_source, _original, _replacement, _inputs, [.. _checks, check]);

    /// <summary>Creates an atomic proposal with a required transformation-specific proof. Failed eligibility returns no proposal; contextual failures retain the finding.</summary>
    public FixProposal? Propose(Func<RewriteEvidence, ProofResult> provesBehavior)
    {
        if (
            _original is not ExpressionSyntax
            || _replacement is null
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
        var text = _replacement.ToString();
        if (_original.ToString() == text)
            return null;
        var edit = SourceChanges.Replace(_source, _original.Span, text);
        return SourceChanges.Propose([edit], context => Validate(context, provesBehavior));
    }

    private bool Validate(RewriteContext context, Func<RewriteEvidence, ProofResult> provesBehavior)
    {
        var memberships = context
            .Original.Sources.Where(source =>
                source.Document.FileIdentity == _source.Document.FileIdentity
            )
            .ToArray();
        return memberships.Length > 0
            && memberships.All(source => ValidateMembership(context, source, provesBehavior));
    }

    private bool ValidateMembership(
        RewriteContext context,
        AnalysisSource source,
        Func<RewriteEvidence, ProofResult> provesBehavior
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
        )
            return false;
        var inputs = ReadInputs(source, mapped);
        if (inputs is null)
            return false;
        var evidence = new RewriteEvidence(context, mapped, inputs);
        return RewriteChecks.SameEnclosingBindings(evidence) == ProofResult.Proven
            && RewriteChecks.SameRetainedBindings(evidence) == ProofResult.Proven
            && RewriteChecks.SameCompilerSuppliedArguments(evidence) == ProofResult.Proven
            && _checks.All(check => check(evidence) == ProofResult.Proven)
            && provesBehavior(evidence) == ProofResult.Proven;
    }

    private IReadOnlyList<InputRewrite>? ReadInputs(AnalysisSource source, NodeRewrite target)
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
            foreach (var marker in _replacement!.GetAnnotatedNodes(input.Annotation))
            {
                var span = new TextSpan(
                    target.After.SpanStart + marker.SpanStart - _replacement.SpanStart,
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

    private static ExpressionSyntax Prepare(ExpressionSyntax expression)
    {
        var clean = expression.WithoutTrivia();
        return (
            clean
                is BinaryExpressionSyntax
                    or ConditionalExpressionSyntax
                    or AssignmentExpressionSyntax
                    or LambdaExpressionSyntax
                    or QueryExpressionSyntax
                ? SyntaxFactory.ParenthesizedExpression(clean)
                : clean
        ).NormalizeWhitespace();
    }
}
