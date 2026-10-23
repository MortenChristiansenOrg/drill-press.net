using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>Short single-line descriptions of source used in withheld-fix explanations.</summary>
internal static class FixReasons
{
    private const int MaximumText = 60;

    /// <summary>Quotes a node's text, shortened to one line, with its one-based position.</summary>
    internal static string Describe(AnalysisSource source, SyntaxNode node) =>
        $"`{Text(node.ToString())}` at {Position(source, node.SpanStart)}";

    internal static string Position(AnalysisSource source, int position)
    {
        var line = source.Tree.GetLineSpan(new(position, 0)).StartLinePosition;
        return $"{line.Line + 1}:{line.Character + 1}";
    }

    internal static string Text(string text)
    {
        var single = string.Join(
            ' ',
            text.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
        );
        return single.Length <= MaximumText ? single : single[..(MaximumText - 3)] + "...";
    }
}
