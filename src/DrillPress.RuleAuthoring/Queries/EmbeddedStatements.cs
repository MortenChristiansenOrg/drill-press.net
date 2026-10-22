using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>The statements C# embeds in another statement without requiring braces.</summary>
internal static class EmbeddedStatements
{
    /// <summary>The embedded statement of an if, else, loop, using, lock or fixed statement, or null for other nodes.</summary>
    internal static StatementSyntax? Of(SyntaxNode? owner) =>
        owner switch
        {
            IfStatementSyntax statement => statement.Statement,
            ElseClauseSyntax clause => clause.Statement,
            ForStatementSyntax loop => loop.Statement,
            CommonForEachStatementSyntax loop => loop.Statement,
            WhileStatementSyntax loop => loop.Statement,
            DoStatementSyntax loop => loop.Statement,
            UsingStatementSyntax statement => statement.Statement,
            LockStatementSyntax statement => statement.Statement,
            FixedStatementSyntax statement => statement.Statement,
            _ => null,
        };

    internal static bool IsEmbedded(StatementSyntax statement) => Of(statement.Parent) == statement;

    /// <summary>A using or fixed statement whose body is another one of the same kind, the conventional unbraced stacking.</summary>
    internal static bool IsStacked(StatementSyntax statement) =>
        statement.Parent is UsingStatementSyntax && statement is UsingStatementSyntax
        || statement.Parent is FixedStatementSyntax && statement is FixedStatementSyntax;
}
