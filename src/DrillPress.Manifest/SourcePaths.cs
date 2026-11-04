namespace DrillPress.Manifest;

/// <summary>
/// Pure string operations on captured source paths. A forward slash always separates segments; a backslash
/// does too on Windows, where it is the native separator. No path is resolved against the filesystem.
/// </summary>
internal static class SourcePaths
{
    private static readonly char[] _invalidCharacters = ['<', '>', ':', '"', '|', '?', '*'];

    /// <summary>The path with forward slashes on Windows; unchanged elsewhere.</summary>
    internal static string Normalize(string path) =>
        OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;

    /// <summary>The last segment of the path.</summary>
    internal static string FileName(string path)
    {
        var normalized = Normalize(path);
        return normalized[(normalized.LastIndexOf('/') + 1)..];
    }

    /// <summary>The path without its last segment, or an empty string for a bare file name.</summary>
    internal static string Directory(string path)
    {
        var index = Normalize(path).LastIndexOf('/');
        return index < 0 ? ""
            : index == 0 ? path[..1]
            : path[..index];
    }

    /// <summary>Whether the path is a file path without empty, current- or parent-directory segments or characters Windows cannot store.</summary>
    internal static bool IsNormalized(string path)
    {
        if (
            string.IsNullOrWhiteSpace(path)
            || path.Any(character => char.IsControl(character))
            || !OperatingSystem.IsWindows() && path.Contains('\\')
        )
            return false;
        var normalized = Normalize(path);
        var segments = normalized.Split('/');
        var first = 0;
        if (normalized.StartsWith("//", StringComparison.Ordinal))
            first = 3;
        else if (normalized.StartsWith('/'))
            first = 1;
        else if (IsDrive(segments[0]))
            first = 1;
        return segments.Length > first
            && segments
                .Skip(first)
                .All(segment =>
                    segment.Length > 0
                    && segment is not ("." or "..")
                    && segment.IndexOfAny(_invalidCharacters) < 0
                    && !segment.EndsWith(' ')
                    && !segment.EndsWith('.')
                );
    }

    /// <summary>Whether the path lies strictly below the directory; an empty directory contains every relative path.</summary>
    internal static bool IsBelow(string path, string directory, bool ignoreCase = false)
    {
        var normalized = Normalize(path);
        var root = Normalize(directory).TrimEnd('/');
        if (directory.Length == 0)
            return !IsRooted(normalized);
        return normalized.Length > root.Length + 1
            && normalized[root.Length] == '/'
            && normalized.StartsWith(
                root,
                ignoreCase || OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal
            );
    }

    /// <summary>The part of a path below the directory, with forward slashes.</summary>
    internal static string Relative(string path, string directory)
    {
        var root = Normalize(directory).TrimEnd('/');
        var normalized = Normalize(path);
        return root.Length == 0 ? normalized : normalized[(root.Length + 1)..];
    }

    /// <summary>The segments of a relative path with forward slashes.</summary>
    internal static string[] Segments(string relative) => relative.Split('/');

    /// <summary>Appends a relative path with forward slashes to a directory, using the directory's own separator.</summary>
    internal static string Join(string directory, string relative)
    {
        if (directory.Length == 0)
            return relative;
        var separator = OperatingSystem.IsWindows() && directory.Contains('\\') ? '\\' : '/';
        var trimmed = directory.TrimEnd('/', separator);
        return (trimmed.Length == 0 ? directory[..1] : trimmed + separator)
            + relative.Replace('/', separator);
    }

    private static bool IsRooted(string normalized) =>
        normalized.StartsWith('/') || normalized.Split('/')[0] is var first && IsDrive(first);

    private static bool IsDrive(string segment) =>
        OperatingSystem.IsWindows()
        && segment.Length == 2
        && char.IsAsciiLetter(segment[0])
        && segment[1] == ':';
}
