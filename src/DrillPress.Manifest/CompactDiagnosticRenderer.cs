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

    /// <summary>Groups rules, relative files, and physical locations; '+' marks retained common-safe fixes. A final line lists skipped coverage rules.</summary>
    /// <param name="result">The validated and optionally selected diagnostics.</param>
    /// <param name="showFixComplexity">Includes assigned complexity once in each rule header; unclassified rules have no annotation.</param>
    /// <param name="explainCoverage">Adds actionable typed explanations, evaluated context identities, and transported source ranges.</param>
    /// <param name="explainFixes">Adds, under each finding whose proposed fix was withheld, the check that rejected it.</param>
    public string Render(
        ValidatedResult result,
        bool showFixComplexity = false,
        bool explainCoverage = false,
        bool explainFixes = false
    )
    {
        var output = new StringBuilder();
        foreach (
            var rule in result
                .Findings.GroupBy(finding => finding.RuleId)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
        )
        {
            output.Append(rule.Key);
            if (showFixComplexity && rule.First().FixComplexity is { } complexity)
                output
                    .Append(" [fix:")
                    .Append(complexity.ToString().ToLowerInvariant())
                    .Append(']');
            output.Append(' ').Append(rule.First().Message).Append('\n');
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

                    if (finding.Evidence is not null)
                        output.Append(" [").Append(finding.Evidence).Append(']');
                    if (finding.Disposition == FindingDisposition.Review)
                        output.Append(" [review]");
                    if (finding.OutcomeRemediation is not null)
                        output.Append(' ').Append(finding.OutcomeRemediation);
                    output.Append('\n');
                    if (explainCoverage)
                        ExplainCoverage(output, finding);
                    if (explainFixes && finding.FixWithheld is { } reason)
                        output.Append("    fix withheld: ").Append(reason).Append('\n');
                }
            }
        }

        AppendSkippedRules(output, result);
        return output.ToString();
    }

    /// <summary>Counts violations per relative project path, then per assigned fix complexity; review findings are not counted.</summary>
    /// <param name="result">The validated and optionally selected diagnostics.</param>
    /// <remarks>A location reported by several projects, such as a linked file, counts once in each.</remarks>
    public string RenderStatus(ValidatedResult result)
    {
        var output = new StringBuilder();
        foreach (
            var project in result
                .ProjectViolations.GroupBy(count => DisplayPath(count.ProjectPath))
                .OrderBy(group => group.Key, StringComparer.Ordinal)
        )
        {
            output
                .Append(Escape(project.Key))
                .Append(' ')
                .Append(project.Sum(count => count.Count).ToString(CultureInfo.InvariantCulture))
                .Append('\n');
            foreach (var count in project)
                output
                    .Append("  ")
                    .Append(count.FixComplexity?.ToString().ToLowerInvariant() ?? "unspecified")
                    .Append(' ')
                    .Append(count.Count.ToString(CultureInfo.InvariantCulture))
                    .Append('\n');
        }

        AppendSkippedRules(output, result);
        return output.ToString();
    }

    private static void AppendSkippedRules(StringBuilder output, ValidatedResult result)
    {
        if (result.SkippedRules.Length > 0)
            output
                .Append("skipped coverage rules: ")
                .AppendJoin(", ", result.SkippedRules.Select(EscapeRuleId))
                .Append('\n');
    }

    private void ExplainCoverage(StringBuilder output, AggregatedFinding finding)
    {
        foreach (var evidence in finding.Coverage ?? [])
        {
            output
                .Append("    ")
                .Append(evidence.Metric)
                .Append(' ')
                .Append(Escape(evidence.Project))
                .Append(' ')
                .Append(Escape(evidence.Framework))
                .Append(" context=")
                .Append(Escape(evidence.ContextId))
                .Append('\n');
            foreach (var reason in evidence.Reasons)
                output
                    .Append("    ")
                    .Append(reason)
                    .Append(": ")
                    .Append(CoverageEvidenceFormatter.Explain(reason))
                    .Append('\n');
            foreach (var call in evidence.Calls ?? [])
                ExplainCall(output, call);
            foreach (var range in evidence.Ranges)
                output.Append(
                    CultureInfo.InvariantCulture,
                    $"    range {Escape(DisplayPath(range.Path))} {range.Start}+{range.Length}: {range.State.ToString().ToLowerInvariant()}\n"
                );
        }
    }

    private void ExplainCall(StringBuilder output, CoverageCallEvidence call)
    {
        output.Append(
            CultureInfo.InvariantCulture,
            $"    call {Escape(DisplayPath(call.Path))} 0x{call.MethodToken:X} IL_{call.InstructionOffset:X4}: block {call.EntryBlock} {(call.EntryCovered ? "hit" : "not hit")}, {(call.PrefixCannotThrow ? "safe prefix" : "unproven prefix")}"
        );
        if (call.CompletionBlock is { } completion)
            output.Append(
                CultureInfo.InvariantCulture,
                $", completion {completion} {(call.CompletionCovered == true ? "hit" : "not hit")}"
            );
        output.Append('\n');
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

    // Quote IDs that would otherwise blur the comma-separated skipped-rule list.
    private static string EscapeRuleId(string id) =>
        id.Contains(',') || id.Contains('"') || id != id.Trim()
            ? JsonEncodedText.Encode(id).ToString().Insert(0, "\"") + "\""
            : id;

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
