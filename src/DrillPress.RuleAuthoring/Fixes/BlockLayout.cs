using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress.Fixes;

internal sealed class BlockLayout(string newLine, string indent, string unit)
{
    internal string NewLine { get; } = newLine;
    internal string Indent { get; } = indent;
    internal string Unit { get; } = unit;

    internal static BlockLayout Read(SourceText text, IfStatementSyntax owner)
    {
        var line = text.Lines.GetLineFromPosition(owner.SpanStart);
        var prefix = text.ToString(TextSpan.FromBounds(line.Start, owner.SpanStart));
        var indent = new string(prefix.TakeWhile(character => character is ' ' or '\t').ToArray());
        var breaks = text.Lines.FirstOrDefault(candidate =>
            candidate.SpanIncludingLineBreak.Length > candidate.Span.Length
        );
        var newline =
            breaks.SpanIncludingLineBreak.Length > breaks.Span.Length
                ? text.ToString(TextSpan.FromBounds(breaks.End, breaks.EndIncludingLineBreak))
                : "\n";
        var widths = text
            .Lines.Select(candidate => new string(
                candidate.ToString().TakeWhile(character => character is ' ' or '\t').ToArray()
            ))
            .Where(value => value.StartsWith(indent) && value.Length > indent.Length)
            .OrderBy(value => value.Length)
            .ToArray();
        var unit =
            widths.FirstOrDefault() is { } nested ? nested[indent.Length..]
            : indent.Contains('\t') ? "\t"
            : "    ";
        return new(newline, indent, unit);
    }
}
