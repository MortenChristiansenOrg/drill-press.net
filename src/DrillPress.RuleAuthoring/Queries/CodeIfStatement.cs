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

    /// <summary>The then branch followed by the else branch when present. An else-if continuation is included and marked by <see cref="CodeBranch.IsElseIf"/>.</summary>
    public IReadOnlyList<CodeBranch> Branches => Else is { } other ? [Then, other] : [Then];

    /// <summary>When exactly one branch of an ordinary if/else pair has braces, the branch without them; null otherwise, including for else-if chains. Use <c>Branches().WithoutBraces()</c> to select every unbraced branch.</summary>
    public CodeBranch? InconsistentlyBracedBranch =>
        Else is { IsElseIf: false } other && Then.HasBraces != other.HasBraces
            ? Then.HasBraces
                ? other
                : Then
            : null;

    /// <summary>The complete statement span.</summary>
    public SourceLocation Location => Source.Locate(Syntax.Span);
}
