using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

/// <summary>A physical source comment with its own reportable location and delimiter-free text.</summary>
public sealed class CodeComment : ICodeElement
{
    private readonly TextSpan _span;

    internal CodeComment(AnalysisSource source, SyntaxTrivia trivia)
    {
        Source = source;
        Trivia = trivia;
        var text = source.Tree.GetText(source.Project.CancellationToken);
        var end = trivia.FullSpan.End;
        while (end > trivia.FullSpan.Start && text[end - 1] is '\r' or '\n')
            end--;
        _span = TextSpan.FromBounds(trivia.FullSpan.Start, end);
    }

    /// <summary>The original document membership and semantic context.</summary>
    public AnalysisSource Source { get; }

    /// <summary>The compiler trivia owning this comment, including structured documentation when present.</summary>
    public SyntaxTrivia Trivia { get; }

    /// <summary>The ordinary or documentation comment category.</summary>
    public CodeCommentKind Kind =>
        Trivia.Kind() switch
        {
            SyntaxKind.SingleLineCommentTrivia => CodeCommentKind.SingleLine,
            SyntaxKind.MultiLineCommentTrivia => CodeCommentKind.MultiLine,
            _ => CodeCommentKind.Documentation,
        };

    /// <summary>The physical comment span, including delimiters and excluding the final line break.</summary>
    public SourceLocation Location => Source.Locate(_span);

    /// <summary>Comment text without //, /* */, /// or /** */ delimiters. Whitespace, line endings and interior documentation decoration are preserved.</summary>
    public string Text
    {
        get
        {
            var raw = Source.Tree.GetText(Source.Project.CancellationToken).ToString(_span);
            if (Kind == CodeCommentKind.SingleLine)
                return raw[2..];
            if (Trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                return raw.EndsWith("*/", StringComparison.Ordinal) && raw.Length >= 5
                    ? raw[3..^2]
                    : raw[3..];
            if (Kind == CodeCommentKind.MultiLine)
                return raw.EndsWith("*/", StringComparison.Ordinal) && raw.Length >= 4
                    ? raw[2..^2]
                    : raw[2..];
            var text = SourceText.From(raw);
            return string.Concat(
                text.Lines.Select(line =>
                {
                    var content = text.ToString(line.SpanIncludingLineBreak);
                    var marker = content.IndexOf("///", StringComparison.Ordinal);
                    return marker < 0 ? content : content.Remove(marker, 3);
                })
            );
        }
    }

    /// <summary>The nearest declaration owning the comment's token, including local functions; null for file-level trivia with no declaration owner.</summary>
    public CodeNode<SyntaxNode>? ContainingDeclaration =>
        Trivia
            .Token.Parent?.AncestorsAndSelf()
            .FirstOrDefault(node => node is MemberDeclarationSyntax or LocalFunctionStatementSyntax)
            is { } declaration
            ? new(Source, declaration)
            : null;
}
