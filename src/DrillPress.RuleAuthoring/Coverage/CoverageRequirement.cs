using System.Globalization;

namespace DrillPress;

/// <summary>A coverage requirement usable with the ordinary Require rule declaration.</summary>
public sealed class CoverageRequirement
{
    private readonly double? _minimum;

    internal CoverageRequirement(double? minimum) => _minimum = minimum;

    /// <summary>Reads occurrence evidence after collection; direct rule evaluation without prepared evidence returns unknown.</summary>
    public ExecutionCoverage ExecutionOf(ICodeElement occurrence) =>
        occurrence.Source?.Project.Coverage.ExecutionOf(occurrence) ?? ExecutionCoverage.Unknown;

    internal bool Satisfied<T>(T candidate) =>
        _minimum is null
            ? candidate is ICodeElement element && ExecutionOf(element) == ExecutionCoverage.Covered
            : Measure(candidate) is { Complete: true, Total: > 0 } measurement
                && measurement.Percentage >= _minimum;

    internal string Detail<T>(T candidate)
    {
        if (_minimum is null)
            return candidate is ICodeElement element
                ? $"coverage: {ExecutionOf(element).ToString().ToLowerInvariant()}"
                : "coverage: unknown";
        var measured = Measure(candidate);
        return measured is { Complete: true, Total: > 0 }
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"line coverage: {measured.Percentage:0.##}% ({measured.Covered}/{measured.Total}), required {_minimum:0.##}%"
            )
            : "line coverage: unknown (missing or zero coverable lines)";
    }

    private static CoverageMeasurement Measure<T>(T candidate) =>
        candidate switch
        {
            AnalysisProject project => project.Coverage.Measure(
                project.Sources.Where(source => !source.Document.IsGenerated)
            ),
            CodeFile file => file.Source.Project.Coverage.Measure([file.Source]),
            ICodeElement { Source: { } source } element => source.Project.Coverage.Measure(
                [source],
                element.Location
            ),
            _ => new(0, 0, false),
        };
}
