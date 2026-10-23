using System.Text;
using System.Text.RegularExpressions;

namespace DrillPress;

/// <summary>A case-sensitive slash-normalized path glob. * stays within a segment, ** crosses segments, and **/ also matches no directory.</summary>
public sealed class PathPattern
{
    // Compiling a non-backtracking regex costs far more than matching, and rules often test the same glob per
    // candidate, through symbol extensions such as NameMatches that have no analysis to hold a cache. Each distinct
    // glob therefore compiles once per process. A compiled glob is an immutable function of its text, so sharing it
    // cannot carry state between analyses, and the bound stops globs computed from source from growing the cache.
    private const int MaxCached = 1024;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        string,
        Regex
    > _compiled = new();
    private readonly Regex _pattern;

    /// <summary>Compiles a repository-supplied glob without reading the filesystem.</summary>
    public PathPattern(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        var normalized = pattern.Replace('\\', '/');
        _pattern =
            _compiled.TryGetValue(normalized, out var compiled) ? compiled
            : _compiled.Count < MaxCached ? _compiled.GetOrAdd(normalized, Compile)
            : Compile(normalized);
    }

    private static Regex Compile(string pattern)
    {
        var expression = new StringBuilder();
        for (var index = 0; index < pattern.Length; index++)
        {
            if (pattern[index] == '*' && index + 1 < pattern.Length && pattern[index + 1] == '*')
            {
                index++;
                var directory = index + 1 < pattern.Length && pattern[index + 1] == '/';
                expression.Append(directory ? "(.*/)?" : ".*");
                index += directory ? 1 : 0;
            }
            else
            {
                expression.Append(
                    pattern[index] switch
                    {
                        '*' => "[^/]*",
                        '?' => "[^/]",
                        var character => Regex.Escape(character.ToString()),
                    }
                );
            }
        }
        return new(
            "\\A" + expression + "\\z",
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
            TimeSpan.FromSeconds(1)
        );
    }

    /// <summary>Matches the entire normalized path. Supply an explicitly relative path for repository-relative policies.</summary>
    public bool Matches(string path) => _pattern.IsMatch(path.Replace('\\', '/'));
}
