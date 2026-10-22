using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress.Engine;

/// <summary>One <c>// drillpress-allow RULE-ID[, RULE-ID]: reason</c> comment in one compilation context.</summary>
internal sealed class RuleAllowance
{
    public const string Marker = "drillpress-allow";

    private readonly TextSpan _ruleIdSpan;

    private RuleAllowance(
        CodeComment comment,
        string[] ruleIds,
        TextSpan ruleIdSpan,
        TextSpan scope,
        bool isTrailing,
        string? problem
    )
    {
        Comment = comment;
        RuleIds = ruleIds;
        _ruleIdSpan = ruleIdSpan;
        Scope = scope;
        IsTrailing = isTrailing;
        Problem = problem;
    }

    public CodeComment Comment { get; }

    /// <summary>The named rules in written order.</summary>
    public IReadOnlyList<string> RuleIds { get; }

    /// <summary>The source range in which findings of the named rules start when this allowance allows them.</summary>
    public TextSpan Scope { get; }

    /// <summary>Whether the comment follows code on its line rather than standing alone.</summary>
    public bool IsTrailing { get; }

    /// <summary>Why the allowance is malformed, as single-line evidence; null when it is valid.</summary>
    public string? Problem { get; }

    /// <summary>The physical identity shared by the same comment in every compilation context.</summary>
    public (string FileIdentity, int Start) Identity =>
        (Comment.Source.Document.FileIdentity, Comment.Span.Start);

    /// <summary>Reads an allowance from an ordinary single-line comment; returns null when the comment is not one.</summary>
    public static RuleAllowance? Read(CodeComment comment, RuleCatalog rules)
    {
        var text = comment.Text;
        var body = text.TrimStart();
        var markerEnd = text.Length - body.Length + Marker.Length;
        if (
            !body.StartsWith(Marker, StringComparison.Ordinal)
            || markerEnd < text.Length
                && !char.IsWhiteSpace(text[markerEnd])
                && text[markerEnd] != ':'
        )
            return null;

        var rest = text[markerEnd..];
        var colon = rest.IndexOf(':');
        var list = colon < 0 ? rest : rest[..colon];
        var reason = colon < 0 ? "" : rest[(colon + 1)..];
        var ruleIds = list.Split(',').Select(id => id.Trim()).ToArray();
        var scope = FindScope(comment, out var isTrailing);
        var problems = new List<string>();
        if (ruleIds.Any(id => id.Length == 0))
            problems.Add("missing rule ID");
        if (string.IsNullOrWhiteSpace(reason))
            problems.Add("missing reason");
        problems.AddRange(
            ruleIds
                .Where(id => id.Length > 0 && !rules.Defines(id))
                .Distinct()
                .Select(id => "unknown rule " + Printable(id))
        );
        if (scope is null)
            problems.Add("no code directly below");

        var listStart = comment.Span.Start + "//".Length + markerEnd;
        return new(
            comment,
            ruleIds,
            new(listStart + list.Length - list.TrimStart().Length, list.Trim().Length),
            scope ?? default,
            isTrailing,
            problems.Count == 0 ? null : string.Join("; ", problems)
        );
    }

    /// <summary>Whether this valid allowance names the finding's rule and its scope contains the finding's start.</summary>
    public bool Allows(RuleDiagnostic diagnostic) =>
        Problem is null
        && RuleIds.Contains(diagnostic.Descriptor.Id)
        && Scope.Start <= diagnostic.Location.Start
        && diagnostic.Location.Start < Scope.End;

    /// <summary>Removes the named rules from the comment, or the whole comment when it names no other rule.</summary>
    public FixProposal Without(IReadOnlyCollection<string> ruleIds)
    {
        var source = Comment.Source;
        var text = source.Tree.GetText(source.Project.CancellationToken);
        var line = text.Lines.GetLineFromPosition(Comment.Span.Start);
        var kept = RuleIds.Where(id => !ruleIds.Contains(id)).ToArray();
        var (span, replacement) =
            kept.Length > 0 ? (_ruleIdSpan, string.Join(", ", kept))
            : IsTrailing
                ? (TextSpan.FromBounds(CodeEnd(text, Comment.Span.Start), Comment.Span.End), "")
            : (TextSpan.FromBounds(line.Start, line.EndIncludingLineBreak), "");
        return SourceChanges.Propose(
            [SourceChanges.Replace(source, span, replacement)],
            CommentRemoval.PreservesCode
        );
    }

    // A trailing comment covers its own line. A standalone comment covers the outermost statement or
    // non-namespace declaration starting at the next code token, otherwise that token's line; only
    // further non-blank lines, such as other comments, may separate them.
    private static TextSpan? FindScope(CodeComment comment, out bool isTrailing)
    {
        var text = comment.Source.Tree.GetText(comment.Source.Project.CancellationToken);
        var line = text.Lines.GetLineFromPosition(comment.Span.Start);
        isTrailing = CodeEnd(text, comment.Span.Start) > line.Start;
        if (isTrailing)
            return TextSpan.FromBounds(line.Start, line.EndIncludingLineBreak);

        var token = comment.Trivia.Token;
        if (token.IsKind(SyntaxKind.EndOfFileToken) || token.SpanStart < comment.Span.End)
            return null;
        var codeLine = text.Lines.GetLineFromPosition(token.SpanStart);
        for (var number = line.LineNumber + 1; number < codeLine.LineNumber; number++)
        {
            if (string.IsNullOrWhiteSpace(text.Lines[number].ToString()))
                return null;
        }

        return token
                .Parent?.AncestorsAndSelf()
                .LastOrDefault(node => IsScope(node) && StartsAt(node, token))
                ?.Span
            ?? TextSpan.FromBounds(codeLine.Start, codeLine.EndIncludingLineBreak);
    }

    private static bool IsScope(SyntaxNode node) =>
        node
            is StatementSyntax
                or AccessorDeclarationSyntax
                or MemberDeclarationSyntax
                and not BaseNamespaceDeclarationSyntax;

    private static bool StartsAt(SyntaxNode node, SyntaxToken token)
    {
        var attributes = node switch
        {
            MemberDeclarationSyntax member => member.AttributeLists,
            StatementSyntax statement => statement.AttributeLists,
            AccessorDeclarationSyntax accessor => accessor.AttributeLists,
            _ => default,
        };
        return node.GetFirstToken() == token
            || attributes.Count > 0 && attributes[^1].GetLastToken().GetNextToken() == token;
    }

    // The position after the last non-whitespace character that precedes the position on its line.
    private static int CodeEnd(SourceText text, int position)
    {
        var start = text.Lines.GetLineFromPosition(position).Start;
        while (position > start && char.IsWhiteSpace(text[position - 1]))
            position--;
        return position;
    }

    private static string Printable(string id) =>
        id.Any(character => char.IsControl(character) || character is '\u2028' or '\u2029')
            ? System.Text.Json.JsonEncodedText.Encode(id).ToString()
            : id;
}
