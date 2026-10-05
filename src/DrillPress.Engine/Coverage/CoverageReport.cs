using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress.Engine;

internal sealed class CoverageReport(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    internal void Apply(AnalysisProject project, string reportPath, string? symbolIdentity)
    {
        var collected = new Dictionary<string, List<CoverageRange>>();
        var invalid = new HashSet<string>();
        using var stream = _fileSystem.File.OpenRead(reportPath);
        var report = XDocument.Load(stream);
        if (report.Root?.Name != "results")
            throw new InvalidDataException("Coverage collector returned an unsupported report.");
        Read(project, report.Root, collected, symbolIdentity, invalid);
        foreach (
            var (document, ranges) in collected.Where(document => !invalid.Contains(document.Key))
        )
            project.Coverage.Add(document, ranges);
    }

    private void Read(
        AnalysisProject project,
        XElement report,
        Dictionary<string, List<CoverageRange>> collected,
        string? symbolIdentity,
        HashSet<string> invalid
    )
    {
        if (symbolIdentity is null)
            return;
        project.Coverage.Unavailable(CoverageReason.ModuleIdentityMismatch);
        var modules = report
            .Descendants("module")
            .Where(module =>
                (string?)module.Attribute("name") == project.Snapshot.AssemblyName + ".dll"
                && (string?)module.Attribute("id") == symbolIdentity
            )
            .ToArray();
        // Alternate frameworks can share an assembly name; only this build's unique symbol identity contributes.
        if (modules.Length != 1)
            return;
        var module = modules[0];
        project.Coverage.Unavailable(CoverageReason.MissingOrExcludedDocument);
        foreach (var source in project.Sources.Where(source => !source.Document.IsGenerated))
        {
            var files = module
                .Descendants("source_file")
                .Where(file => Matches(source, file))
                .ToArray();
            if (files.Length != 1)
            {
                if (module.Descendants("source_file").Any(file => MatchesPath(source, file)))
                    project.Coverage.DocumentUnavailable(
                        source.Document.DocumentId,
                        CoverageReason.SourceIdentityMismatch
                    );
                continue;
            }
            var id = (string?)files[0].Attribute("id");
            if (string.IsNullOrWhiteSpace(id))
                continue;
            if (!collected.TryGetValue(source.Document.DocumentId, out var ranges))
                collected.Add(source.Document.DocumentId, ranges = []);
            foreach (
                var range in module
                    .Descendants("range")
                    .Where(range => (string?)range.Attribute("source_id") == id)
            )
            {
                var function = range.Ancestors("function").FirstOrDefault();
                var identity =
                    (string?)function?.Attribute("token") ?? (string?)function?.Attribute("id");
                if (Span(source, range) is { } span && !string.IsNullOrWhiteSpace(identity))
                    ranges.Add(
                        new(
                            span,
                            (string?)range.Attribute("covered") switch
                            {
                                "yes" => ExecutionCoverage.Covered,
                                "no" => ExecutionCoverage.Uncovered,
                                _ => ExecutionCoverage.Unknown,
                            },
                            identity
                        )
                    );
                else
                {
                    invalid.Add(source.Document.DocumentId);
                    project.Coverage.DocumentUnavailable(
                        source.Document.DocumentId,
                        CoverageReason.InvalidReportRange
                    );
                }
            }
        }
    }

    private bool MatchesPath(AnalysisSource source, XElement file) =>
        (string?)file.Attribute("path") is { } path
        && string.Equals(
            _fileSystem.Path.GetFullPath(path),
            _fileSystem.Path.GetFullPath(source.Document.Path),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal
        );

    private bool Matches(AnalysisSource source, XElement file)
    {
        if (!MatchesPath(source, file) || !_fileSystem.File.Exists(source.Document.Path))
            return false;
        var bytes = _fileSystem.File.ReadAllBytes(source.Document.Path);
        var checksum = (string?)file.Attribute("checksum_type") switch
        {
            "SHA256" => Convert.ToHexString(SHA256.HashData(bytes)),
            "SHA1" => Convert.ToHexString(SHA1.HashData(bytes)),
            _ => "",
        };
        return checksum.Length > 0
            && checksum.Equals(
                (string?)file.Attribute("checksum"),
                StringComparison.OrdinalIgnoreCase
            )
            && _fileSystem.File.ReadAllText(source.Document.Path) == source.Document.Text;
    }

    private static TextSpan? Span(AnalysisSource source, XElement range)
    {
        var text = source.Tree.GetText();
        if (
            !int.TryParse((string?)range.Attribute("start_line"), out var startLine)
            || !int.TryParse((string?)range.Attribute("end_line"), out var endLine)
            || !int.TryParse((string?)range.Attribute("start_column"), out var startColumn)
            || !int.TryParse((string?)range.Attribute("end_column"), out var endColumn)
            || startLine < 1
            || endLine < startLine
            || endLine > text.Lines.Count
            || startColumn < 1
            || endColumn < 1
            || startColumn > text.Lines[startLine - 1].Span.Length + 1
            || endColumn > text.Lines[endLine - 1].Span.Length + 1
        )
            return null;
        var start = text.Lines[startLine - 1].Start + startColumn - 1;
        var end = text.Lines[endLine - 1].Start + endColumn - 1;
        return end > start ? TextSpan.FromBounds(start, end) : null;
    }
}
