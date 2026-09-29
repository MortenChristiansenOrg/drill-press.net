using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

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

    /// <summary>Adds named bounded invariants to the default source, binding, trivia and compiler-supplied-argument checks. Evaluation preservation and transformation-specific equivalence still require explicit proof.</summary>
    public FixBuilder MustPreserve(Behavior behavior) =>
        BehaviorChecks
            .Expressions(behavior)
            .Aggregate(this, (builder, check) => builder.Require(check));

    /// <summary>Registers exact retained input occurrences under readable construction syntax.</summary>
    public FixBuilder Keeping(params ExpressionInput[] inputs) => MapInputs(inputs);

    /// <summary>Provides the required behavior proof after all default and added gates. False means Unknown and withholds the proposal.</summary>
    public FixProposal? SafeWhen(Func<RewriteEvidence, bool> proof) =>
        Propose(change => proof(change) ? ProofResult.Proven : ProofResult.Unknown);

    /// <summary>Provides a tri-state behavior proof after all default and added gates.</summary>
    public FixProposal? SafeWhen(Func<RewriteEvidence, ProofResult> proof) => Propose(proof);

    /// <summary>Replaces an expression and automatically registers every supplied template input, including repeated/unused holes.</summary>
    public FixBuilder ReplaceWith(ExpressionTemplate replacement) =>
        ReplaceWith(replacement.Syntax).MapInputs(replacement.Inputs.ToArray());

    /// <summary>Constructs equality from explicit operands; optionally absorbs an enclosing built-in Boolean negation into inequality. This construction does not prove call/operator equivalence.</summary>
    public FixBuilder ReplaceWithEquality(
        ExpressionInput left,
        ExpressionInput right,
        bool absorbNegation = false
    )
    {
        SyntaxNode target = _original;
        while (target.Parent is ParenthesizedExpressionSyntax parentheses)
            target = parentheses;
        if (
            absorbNegation
            && target.Parent is PrefixUnaryExpressionSyntax negation
            && negation.IsKind(SyntaxKind.LogicalNotExpression)
            && _source.Model.GetOperation(negation, _source.Project.CancellationToken)
                is IUnaryOperation
                {
                    OperatorMethod: null,
                    Type.SpecialType: SpecialType.System_Boolean
                }
        )
            return new FixBuilder(_source, negation, inputs: _inputs, checks: _checks)
                .ReplaceWith(Code.Expression("{0} != {1}", left, right))
                .Require(change =>
                    change.BeforeModel.GetOperation(change.Before)
                        is IUnaryOperation
                        {
                            OperatorMethod: null,
                            Type.SpecialType: SpecialType.System_Boolean
                        }
                        ? ProofResult.Proven
                        : ProofResult.Unknown
                );
        return ReplaceWith(Code.Equal(left, right));
    }

    /// <summary>Replaces an expression, adding parentheses for compound replacement syntax and retaining exterior trivia.</summary>
    public FixBuilder ReplaceWith(ExpressionSyntax replacement) =>
        new(_source, _original, Prepare(replacement), _inputs, _checks);

    /// <summary>Registers annotated retained inputs. Mappings are verified after reparsing in every context.</summary>
    public FixBuilder MapInputs(params ExpressionInput[] inputs) =>
        new(_source, _original, _replacement, inputs.ToArray(), _checks);

    /// <summary>Adds an invariant; unknown or disproven evidence withholds the complete proposal.</summary>
    /// <remarks>Source must be editable, nongenerated and free of interior comments/directives, nameof and expression-tree contexts. Every affected compilation must compile and preserve enclosing bindings, retained-input bindings/conversions and compiler-supplied arguments. Evaluation counts/order, receiver null behavior and transformation-specific equivalence are separate obligations.</remarks>
    public FixBuilder Require(Func<RewriteEvidence, ProofResult> check) =>
        new(_source, _original, _replacement, _inputs, [.. _checks, check]);

    /// <summary>Selects a written modifier for removal under declaration-specific proofs.</summary>
    public ModifierRemoval RemoveModifier(Modifier modifier) =>
        RemoveModifier((SyntaxKind)modifier);

    /// <summary>Adds braces to an existing if/else branch under the restricted structural proof.</summary>
    public BlockWrapping AddBraces() => WrapInBlock();

    /// <summary>Selects one actual declaration modifier token for removal. Declaration and behavior invariants remain explicit.</summary>
    public ModifierRemoval RemoveModifier(SyntaxKind kind) =>
        new(_source, _original, kind, _checks);

    /// <summary>Wraps an actual if/else embedded statement in a block under the restricted structural proof.</summary>
    public BlockWrapping WrapInBlock() => new(_source, _original, _checks);

    /// <summary>Selects one explicit bound parameter value for removal under an exact contextual method transition.</summary>
    public ArgumentRemoval RemoveArgument(string parameter) =>
        new(_source, _original, parameter, _checks);

    /// <summary>Creates an atomic proposal with a required transformation-specific proof. Failed eligibility returns no proposal; contextual failures retain the finding.</summary>
    /// <remarks>Source must be editable, nongenerated and free of interior comments/directives, nameof and expression-tree contexts. Every affected compilation must compile and preserve enclosing bindings, retained-input bindings/conversions and compiler-supplied arguments. Evaluation counts/order, receiver null behavior and transformation-specific equivalence are separate obligations.</remarks>
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
                is IdentifierNameSyntax
                    or GenericNameSyntax
                    or LiteralExpressionSyntax
                    or ParenthesizedExpressionSyntax
                    or MemberAccessExpressionSyntax
                    or InvocationExpressionSyntax
                    or ElementAccessExpressionSyntax
                    or BaseObjectCreationExpressionSyntax
                    or ThisExpressionSyntax
                    or BaseExpressionSyntax
                    or TypeOfExpressionSyntax
                    or DefaultExpressionSyntax
                    or InterpolatedStringExpressionSyntax
                    or TupleExpressionSyntax
                    or PredefinedTypeSyntax
                ? clean
                : SyntaxFactory.ParenthesizedExpression(clean)
        ).NormalizeWhitespace();
    }
}
