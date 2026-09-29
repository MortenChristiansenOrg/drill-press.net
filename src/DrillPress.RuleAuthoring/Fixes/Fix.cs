using DrillPress;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>Constructs contextual edit proposals; no files are written and a consumer semantic proof is required.</summary>
public static class Fix
{
    /// <summary>Starts a proposal anchored to this exact declaration part.</summary>
    public static FixBuilder For(CodeTypeDeclaration declaration) =>
        new(declaration.Source, declaration.Syntax);

    /// <summary>Starts a proposal for an existing conditional branch.</summary>
    public static FixBuilder For(CodeBranch branch) => new(branch.Source, branch.Syntax);

    /// <summary>Starts atomic member extraction from explicitly selected and semantically grouped occurrences.</summary>
    public static ExpressionExtraction Extract(DrillPress.ExpressionGroup group) => new(group);

    /// <summary>Starts a proposal for an exact original syntax candidate.</summary>
    public static FixBuilder For<TSyntax>(CodeNode<TSyntax> node)
        where TSyntax : SyntaxNode => new(node.Source, node.Syntax);

    /// <summary>Starts a proposal for an original source node.</summary>
    public static FixBuilder For(AnalysisSource source, SyntaxNode syntax) => new(source, syntax);

    /// <summary>Starts a proposal for a bound invocation.</summary>
    public static FixBuilder For(CodeInvocation call) => new(call.Source, call.Operation.Syntax);

    /// <summary>Starts a proposal for the selected ordinary part of a named type.</summary>
    public static FixBuilder For(CodeDeclaration declaration) =>
        new(declaration.Source, declaration.Syntax);

    /// <summary>Starts a proposal for an ordinary method declaration.</summary>
    public static FixBuilder For(CodeMethod method) => new(method.Source, method.Syntax);

    /// <summary>Creates an occurrence marker to embed in replacement syntax and register with MapInputs.</summary>
    public static ExpressionInput Input<TSyntax>(CodeNode<TSyntax> expression)
        where TSyntax : ExpressionSyntax => new(expression.Source, expression.Syntax);

    /// <summary>Tracks an original source operand when constructing custom replacement syntax.</summary>
    public static ExpressionInput Input(AnalysisSource source, ExpressionSyntax expression) =>
        new(source, expression);
}
