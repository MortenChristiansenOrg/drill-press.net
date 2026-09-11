using DrillPress.Manifest;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress.Testing;

/// <summary>Validated rule output and in-memory after-fix source for exact consumer assertions.</summary>
public sealed class RuleTestResult
{
    private readonly CompilationSnapshot _snapshot;
    private readonly ValidatedResult _result;

    internal RuleTestResult(CompilationSnapshot snapshot, ValidatedResult result)
    {
        _snapshot = snapshot;
        _result = result;
        var files = snapshot
            .Projects.SelectMany(project => project.Documents)
            .GroupBy(document => document.FileIdentity)
            .ToDictionary(group => group.Key, group => group.First());
        Findings = result
            .Findings.Select(finding => new TestFinding(
                finding.RuleId,
                finding.Path,
                finding.Line,
                finding.Column,
                files[finding.FileIdentity].Text.Substring(finding.Start, finding.Length),
                finding.BatchId is not null
            ))
            .ToArray();
    }

    /// <summary>Physical findings after agreement across contexts, including withheld fixes.</summary>
    public IReadOnlyList<TestFinding> Findings { get; }

    /// <summary>Applies only the validated plan to captured text. No OS files are written.</summary>
    public string FixedText(string path)
    {
        var document = _snapshot
            .Projects.SelectMany(project => project.Documents)
            .First(document => document.Path == path);
        return SourceText
            .From(document.Text)
            .WithChanges(
                _result
                    .Edits.Where(edit => edit.FileIdentity == document.FileIdentity)
                    .Select(edit => new TextChange(new(edit.Start, edit.Length), edit.Replacement))
            )
            .ToString();
    }
}
