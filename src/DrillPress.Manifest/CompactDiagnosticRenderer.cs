using System.Globalization;
using System.IO.Abstractions;
using System.Text;
using System.Text.Json;

namespace DrillPress.Manifest;

/// <summary>Renders the single public diagnostic format with LF and culture-independent coordinates.</summary>
public sealed class CompactDiagnosticRenderer
{
    private readonly IFileSystem _fileSystem;

    /// <summary>Resolves display paths relative to the local invocation directory.</summary>
    public CompactDiagnosticRenderer()
        : this(new FileSystem()) { }

    internal CompactDiagnosticRenderer(IFileSystem fileSystem) => _fileSystem = fileSystem;

    /// <summary>Groups rules, relative files, and physical locations; '+' marks retained common-safe fixes.</summary>
    public string Render(ValidatedResult result)
    {
        var output = new StringBuilder();
        foreach (
            var rule in result
                .Findings.GroupBy(finding => finding.RuleId)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
        )
        {
            output.Append(rule.Key).Append(' ').Append(rule.First().Message).Append('\n');
            foreach (
                var file in rule.GroupBy(finding => DisplayPath(finding.Path))
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
            )
            {
                output.Append(Escape(file.Key)).Append('\n');
                foreach (
                    var finding in file.OrderBy(finding => finding.Start)
                        .ThenBy(finding => finding.Length)
                )
                {
                    output.Append("  ");
                    if (finding.BatchId is not null)
                    {
                        output.Append('+');
                    }

                    output.Append(finding.Line.ToString(CultureInfo.InvariantCulture));
                    if (finding.Column != 1)
                    {
                        output
                            .Append(':')
                            .Append(finding.Column.ToString(CultureInfo.InvariantCulture));
                    }

                    output.Append('\n');
                }
            }
        }

        return output.ToString();
    }

    private string DisplayPath(string path)
    {
        var currentDirectory = _fileSystem.Directory.GetCurrentDirectory();
        var directory = _fileSystem.Path.GetDirectoryName(path);
        var relativeDirectory = _fileSystem.Path.GetRelativePath(
            currentDirectory,
            string.IsNullOrEmpty(directory) ? currentDirectory : directory
        );
        var fileName = _fileSystem.Path.GetFileName(path);
        var displayPath =
            relativeDirectory == "."
                ? fileName
                : _fileSystem.Path.Combine(relativeDirectory, fileName);
        return displayPath.Replace(_fileSystem.Path.DirectorySeparatorChar, '/');
    }

    private static string Escape(string path) =>
        path.Any(char.IsControl)
        || path.Contains('\u2028')
        || path.Contains('\u2029')
        || path.Contains('\\')
        || path.Contains('"')
        || path != path.Trim()
            ? JsonEncodedText.Encode(path).ToString().Insert(0, "\"") + "\""
            : path;
}
