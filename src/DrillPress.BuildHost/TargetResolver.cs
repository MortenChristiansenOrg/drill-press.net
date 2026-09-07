using System.IO.Abstractions;
using System.Text.RegularExpressions;

namespace DrillPress.BuildHost;

internal sealed class TargetResolver(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    public string Resolve(string target)
    {
        var path = _fileSystem.Path.GetFullPath(target);
        if (target.IndexOfAny(['*', '?']) >= 0)
        {
            return path;
        }

        if (_fileSystem.Directory.Exists(path))
        {
            var files = _fileSystem.Directory.GetFiles(path);
            var solutions = files.Where(file => HasExtension(file, ".sln", ".slnx")).ToArray();
            var candidates = solutions.Length > 0 ? solutions : files.Where(file => HasExtension(file, ".csproj")).ToArray();
            return candidates.Length == 1 ? candidates[0] : throw new InvalidOperationException(
                $"Directory '{target}' contains {candidates.Length} eligible targets; specify a .sln, .slnx, or .csproj file.");
        }

        if (!_fileSystem.File.Exists(path) || !HasExtension(path, ".csproj", ".sln", ".slnx", ".cs"))
        {
            throw new FileNotFoundException($"C# target '{target}' was not found or has an unsupported extension.", path);
        }

        return path;
    }

    public string[] ExpandSources(string target)
    {
        if (target.IndexOfAny(['*', '?']) < 0)
        {
            return [target];
        }

        var normalized = target.Replace(_fileSystem.Path.DirectorySeparatorChar, '/');
        var wildcard = normalized.IndexOfAny(['*', '?']);
        var separator = normalized.LastIndexOf('/', wildcard);
        var root = normalized[..(separator + 1)];
        var pattern = normalized[(separator + 1)..];
        var expression = new System.Text.StringBuilder("^");
        for (var index = 0; index < pattern.Length; index++)
        {
            if (pattern[index] == '*' && index + 1 < pattern.Length && pattern[index + 1] == '*')
            {
                index++;
                if (index + 1 < pattern.Length && pattern[index + 1] == '/')
                {
                    index++;
                    expression.Append("(?:.*/)?");
                }
                else
                {
                    expression.Append(".*");
                }
            }
            else
            {
                expression.Append(pattern[index] switch
                {
                    '*' => "[^/]*",
                    '?' => "[^/]",
                    var character => Regex.Escape(character.ToString()),
                });
            }
        }

        expression.Append('$');
        var matcher = new Regex(expression.ToString(), RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        var matches = _fileSystem.Directory.Exists(root)
            ? _fileSystem.Directory.EnumerateFiles(root, "*", new EnumerationOptions
                { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint })
                .Where(file => HasExtension(file, ".cs"))
                .Where(file => matcher.IsMatch(_fileSystem.Path.GetRelativePath(root, file).Replace(_fileSystem.Path.DirectorySeparatorChar, '/')))
                .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray()
            : [];
        return matches.Length > 0 ? matches : throw new FileNotFoundException($"C# glob '{target}' matched no files.");
    }

    private bool HasExtension(string path, params string[] extensions) =>
        extensions.Contains(_fileSystem.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
