using DrillPress;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>Constructs contextual edit proposals; no files are written and a consumer semantic proof is required.</summary>
public static class Fix
{
    /// <summary>Selects physical comment trivia for removal with token and caller-information preservation checks.</summary>
    public static CommentFix For(CodeComment comment) => new(comment);

    /// <summary>Selects a written local, foreach or out type for inference-preserving replacement.</summary>
    public static TypedDeclarationFix For(CodeTypedDeclaration declaration) => new(declaration);

    /// <summary>Starts an expression edit; a source-less member reference retains an unavailable builder that proposes nothing.</summary>
    /// <remarks>Selection does not authorize an edit. See the selected builder Propose documentation for its default source, trivia, binding and contextual compilation gates; additional evaluation and behavior obligations remain explicit.</remarks>
    public static ReferenceFix For(MemberReference reference) =>
        new(
            reference.Source is { } source && reference.Syntax is { } syntax
                ? new FixBuilder(source, syntax)
                : null
        );

    /// <summary>Selects a bound argument for an explicit removal operation.</summary>
    /// <remarks>Selection does not authorize an edit. See the selected builder Propose documentation for its default source, trivia, binding and contextual compilation gates; additional evaluation and behavior obligations remain explicit.</remarks>
    public static ArgumentFix For(CodeArgument argument) => new(argument);

    /// <summary>Starts an expression edit on its original source.</summary>
    /// <remarks>Selection does not authorize an edit. See the selected builder Propose documentation for its default source, trivia, binding and contextual compilation gates; additional evaluation and behavior obligations remain explicit.</remarks>
    public static FixBuilder For(CodeExpression expression) =>
        new(expression.Source, expression.Syntax);

    /// <summary>Tracks an original expression occurrence for typed template construction.</summary>
    public static ExpressionInput Input(CodeExpression expression) =>
        new(expression.Source, expression.Syntax);

    /// <summary>Starts a proposal anchored to this exact declaration part.</summary>
    /// <remarks>Selection does not authorize an edit. See the selected builder Propose documentation for its default source, trivia, binding and contextual compilation gates; additional evaluation and behavior obligations remain explicit.</remarks>
    public static FixBuilder For(CodeTypeDeclaration declaration) =>
        new(declaration.Source, declaration.Syntax);

    /// <summary>Starts a proposal for an existing conditional branch.</summary>
    /// <remarks>Selection does not authorize an edit. See the selected builder Propose documentation for its default source, trivia, binding and contextual compilation gates; additional evaluation and behavior obligations remain explicit.</remarks>
    public static FixBuilder For(CodeBranch branch) => new(branch.Source, branch.Syntax);

    /// <summary>Starts atomic member extraction from explicitly selected and semantically grouped occurrences.</summary>
    public static ExpressionExtraction Extract(DrillPress.ExpressionGroup group) => new(group);

    /// <summary>Starts a proposal for an exact original syntax candidate.</summary>
    /// <remarks>Selection does not authorize an edit. See the selected builder Propose documentation for its default source, trivia, binding and contextual compilation gates; additional evaluation and behavior obligations remain explicit.</remarks>
    public static FixBuilder For<TSyntax>(CodeNode<TSyntax> node)
        where TSyntax : SyntaxNode => new(node.Source, node.Syntax);

    /// <summary>Starts a proposal for an original source node.</summary>
    /// <remarks>Selection does not authorize an edit. See the selected builder Propose documentation for its default source, trivia, binding and contextual compilation gates; additional evaluation and behavior obligations remain explicit.</remarks>
    public static FixBuilder For(AnalysisSource source, SyntaxNode syntax) => new(source, syntax);

    /// <summary>Starts a proposal for a bound invocation.</summary>
    /// <remarks>Selection does not authorize an edit. See the selected builder Propose documentation for its default source, trivia, binding and contextual compilation gates; additional evaluation and behavior obligations remain explicit.</remarks>
    public static FixBuilder For(CodeInvocation call) => new(call.Source, call.Operation.Syntax);

    /// <summary>Starts a proposal for the selected ordinary part of a named type.</summary>
    /// <remarks>Selection does not authorize an edit. See the selected builder Propose documentation for its default source, trivia, binding and contextual compilation gates; additional evaluation and behavior obligations remain explicit.</remarks>
    public static FixBuilder For(CodeDeclaration declaration) =>
        new(declaration.Source, declaration.Syntax);

    /// <summary>Starts a proposal for an ordinary method declaration.</summary>
    /// <remarks>Selection does not authorize an edit. See the selected builder Propose documentation for its default source, trivia, binding and contextual compilation gates; additional evaluation and behavior obligations remain explicit.</remarks>
    public static FixBuilder For(CodeMethod method) => new(method.Source, method.Syntax);

    /// <summary>Creates an occurrence marker to embed in replacement syntax and register with MapInputs.</summary>
    public static ExpressionInput Input<TSyntax>(CodeNode<TSyntax> expression)
        where TSyntax : ExpressionSyntax => new(expression.Source, expression.Syntax);

    /// <summary>Tracks an original source operand when constructing custom replacement syntax.</summary>
    public static ExpressionInput Input(AnalysisSource source, ExpressionSyntax expression) =>
        new(source, expression);
}
