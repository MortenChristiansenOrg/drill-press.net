using System.Text;

namespace DrillPress;

/// <summary>Shared word boundaries for C# identifier naming conventions; returned words preserve their original casing.</summary>
public static class CodeIdentifier
{
    /// <summary>Splits underscores, lower-to-upper and acronym-to-word transitions, and both letter/digit transitions. Leading verbatim prefixes and empty segments are ignored.</summary>
    public static IReadOnlyList<string> Words(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        var words = new List<string>();
        foreach (
            var segment in identifier
                .TrimStart('@')
                .Split('_', StringSplitOptions.RemoveEmptyEntries)
        )
        {
            var start = 0;
            var previous = Rune.GetRuneAt(segment, 0);
            for (var index = previous.Utf16SequenceLength; index < segment.Length; )
            {
                var current = Rune.GetRuneAt(segment, index);
                var next = index + current.Utf16SequenceLength;
                var nextIsLower =
                    next < segment.Length && Rune.IsLower(Rune.GetRuneAt(segment, next));
                if (
                    Rune.IsDigit(previous) != Rune.IsDigit(current)
                    || Rune.IsUpper(current)
                        && (Rune.IsLower(previous) || Rune.IsUpper(previous) && nextIsLower)
                )
                {
                    words.Add(segment[start..index]);
                    start = index;
                }
                previous = current;
                index = next;
            }
            words.Add(segment[start..]);
        }
        return words.AsReadOnly();
    }

    /// <summary>Matches one complete word using ordinal case-sensitive comparison unless explicitly configured otherwise.</summary>
    public static bool ContainsWord(
        string identifier,
        string word,
        StringComparison comparison = StringComparison.Ordinal
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        return Words(identifier).Any(candidate => candidate.Equals(word, comparison));
    }

    internal static IReadOnlyList<string> NamedWords(string name) =>
        name.Length == 0 ? [] : Words(name);

    internal static bool NamedContainsWord(string name, string word, StringComparison comparison)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        return NamedWords(name).Any(candidate => candidate.Equals(word, comparison));
    }
}
