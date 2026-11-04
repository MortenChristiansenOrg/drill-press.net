using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>An inlined local in one affected compilation, after the library's gates and binding checks held there, for stating why the initializer may move past the evaluation it now follows.</summary>
public sealed class InlineChange
{
    internal InlineChange(LocalInliningPlan plan, RewriteContext rewrite)
    {
        var source = plan.Source;
        Declaration = new(source, plan.Declaration.Declaration, plan.Declaration.Declaration.Type);
        Initializer = new(source, plan.Initializer);
        Read = new(source, plan.Read);
        SkippedStatements = Array.AsReadOnly(
            plan.SkippedStatements.Select(statement => new CodeNode<StatementSyntax>(
                    source,
                    statement
                ))
                .ToArray()
        );
        SkippedOperands = Array.AsReadOnly(
            plan.SkippedOperations.Where(operation =>
                    !operation.IsImplicit && operation.Syntax is ExpressionSyntax
                )
                .Select(operation => new CodeExpression(source, (ExpressionSyntax)operation.Syntax))
                .ToArray()
        );
        Rewrite = rewrite;
    }

    /// <summary>The removed declaration in this compilation's original source.</summary>
    public CodeVariableDeclaration Declaration { get; }

    /// <summary>The initializer, before it moves.</summary>
    public CodeExpression Initializer { get; }

    /// <summary>The local's single read, which the initializer replaces.</summary>
    public CodeExpression Read { get; }

    /// <summary>The statements between the declaration and the read's statement, which now run before the initializer. Empty when the read is in the next statement.</summary>
    public IReadOnlyList<CodeNode<StatementSyntax>> SkippedStatements { get; }

    /// <summary>The outermost written operands that the read's statement evaluates before the read, such as a receiver, an earlier argument or the object creation whose initializer contains the read, which now run before the initializer.</summary>
    public IReadOnlyList<CodeExpression> SkippedOperands { get; }

    /// <summary>The complete rewritten compilation and edit batch being validated.</summary>
    public RewriteContext Rewrite { get; }
}
