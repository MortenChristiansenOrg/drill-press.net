using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>An existing conditional branch, including an else-if continuation.</summary>
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

    /// <summary>The complete statement span.</summary>
    public SourceLocation Location => Source.Locate(Syntax.Span);
}
