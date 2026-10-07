using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>Bridges raw syntax selections to the expression model, which has type, constant and context helpers and can be fixed.</summary>
public static class CodeNodeQueries
{
    /// <summary>Views an expression node as a <see cref="CodeExpression"/> in the same compilation.</summary>
    public static CodeExpression AsExpression<TSyntax>(this CodeNode<TSyntax> node)
        where TSyntax : ExpressionSyntax => new(node.Source, node.Syntax);

    /// <summary>Views selected expression nodes as <see cref="CodeExpression"/> candidates.</summary>
    public static CodeQuery<CodeExpression> Expressions<TSyntax>(
        this CodeQuery<CodeNode<TSyntax>> nodes
    )
        where TSyntax : ExpressionSyntax => nodes.Select(node => node.AsExpression());
}
