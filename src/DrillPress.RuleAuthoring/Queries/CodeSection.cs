using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

/// <summary>A run of a block's statements without a blank line between them, such as the Arrange, Act or Assert section of a test.</summary>
public sealed class CodeSection : ICodeElement
{
    private readonly NestedFunctions _nested;

    internal CodeSection(
        AnalysisSource source,
        IReadOnlyList<StatementSyntax> statements,
        int index,
        NestedFunctions nested
    )
    {
        Source = source;
        Statements = statements;
        Index = index;
        _nested = nested;
    }

    /// <summary>The original document membership.</summary>
    public AnalysisSource Source { get; }

    /// <summary>The section's statements in source order; never empty.</summary>
    public IReadOnlyList<StatementSyntax> Statements { get; }

    /// <summary>The section's zero-based position in its body.</summary>
    public int Index { get; }

    /// <summary>From the first statement's start to the last statement's end.</summary>
    public SourceLocation Location =>
        Source.Locate(TextSpan.FromBounds(Statements[0].SpanStart, Statements[^1].Span.End));

    /// <summary>Bound calls in the section's statements, honoring the body's nested-function policy.</summary>
    public IEnumerable<CodeInvocation> Calls() => Bodies().SelectMany(body => body.Calls());

    /// <summary>Syntax in the section's statements, honoring the body's nested-function policy.</summary>
    public IEnumerable<CodeNode<TSyntax>> Nodes<TSyntax>()
        where TSyntax : SyntaxNode => Bodies().SelectMany(body => body.Nodes<TSyntax>());

    private IEnumerable<CodeBody> Bodies() =>
        Statements.Select(statement => CodeBody.Of(Source, statement, _nested));
}
