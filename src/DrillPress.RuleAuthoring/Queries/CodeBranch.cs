using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>An embedded statement: an if or else branch, including an else-if continuation, or the body of a for, foreach, while, do, using, lock or fixed statement.</summary>
public sealed class CodeBranch(AnalysisSource source, StatementSyntax syntax) : ICodeElement
{
    /// <summary>The original compiler membership.</summary>
    public AnalysisSource Source { get; } = source;

    /// <summary>The original embedded statement or block.</summary>
    public StatementSyntax Syntax { get; } = syntax;

    /// <summary>Whether the branch is explicitly surrounded by braces.</summary>
    public bool HasBraces => Syntax is BlockSyntax;

    /// <summary>Whether an else branch continues an else-if chain.</summary>
    public bool IsElseIf => Syntax is IfStatementSyntax { Parent: ElseClauseSyntax };

    /// <summary>Whether a using or fixed statement is the body of another one of the same kind, as in <c>using (a) using (b) { }</c>.</summary>
    public bool IsStacked => EmbeddedStatements.IsStacked(Syntax);

    /// <summary>The complete statement span.</summary>
    public SourceLocation Location => Source.Locate(Syntax.Span);
}
