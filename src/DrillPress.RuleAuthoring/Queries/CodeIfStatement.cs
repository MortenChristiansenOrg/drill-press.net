using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>A written if statement with reportable branches; missing else branches stay absent.</summary>
public sealed class CodeIfStatement(AnalysisSource source, IfStatementSyntax syntax) : ICodeElement
{
    /// <summary>The original compiler membership.</summary>
    public AnalysisSource Source { get; } = source;

    /// <summary>The complete if statement.</summary>
    public IfStatementSyntax Syntax { get; } = syntax;

    /// <summary>The condition-true branch.</summary>
    public CodeBranch Then => new(Source, Syntax.Statement);

    /// <summary>The condition-false branch, possibly another if statement.</summary>
    public CodeBranch? Else => Syntax.Else is { } clause ? new(Source, clause.Statement) : null;

    /// <summary>The unbraced side when exactly one ordinary branch has braces; else-if chains produce no suggestion.</summary>
    public CodeBranch? BranchWithoutBraces =>
        Else is { IsElseIf: false } other && Then.HasBraces != other.HasBraces
            ? Then.HasBraces
                ? other
                : Then
            : null;

    /// <summary>The complete statement span.</summary>
    public SourceLocation Location => Source.Locate(Syntax.Span);
}
