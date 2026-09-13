using System.Text;
using System.Text.RegularExpressions;

namespace DrillPress.Configuration;

/// <summary>A case-sensitive slash-normalized path glob. * stays within a segment, ** crosses segments, and **/ also matches no directory.</summary>
public sealed class PathPattern
{
    private readonly Regex _pattern;

    /// <summary>Compiles a repository-supplied glob without reading the filesystem.</summary>
    public PathPattern(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        pattern = pattern.Replace('\\', '/');
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
        _pattern = new(
            "\\A" + expression + "\\z",
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
            TimeSpan.FromSeconds(1)
        );
    }

    /// <summary>Matches the entire normalized path. Supply an explicitly relative path for repository-relative policies.</summary>
    public bool Matches(string path) => _pattern.IsMatch(path.Replace('\\', '/'));
}
