namespace DrillPress;

/// <summary>Declares line thresholds for files, source elements, and evaluated projects.</summary>
public sealed class LineCoverage
{
    internal LineCoverage() { }

    /// <summary>Reads prepared physical line counts for a project, file, method, type, or other source element; does not launch tests.</summary>
    public LineCoverageMeasurement Measure<T>(T candidate) =>
        candidate switch
        {
            AnalysisProject project => project.Coverage.Measure(
                project.Sources.Where(source => !source.Document.IsGenerated)
            ),
            CodeFile file => file.Source.Project.Coverage.Measure([file.Source]),
            CodeMethod method => method.Source.Project.Coverage.Measure(
                [method.Source],
                method.Source.Locate(method.Syntax.Span)
            ),
            CodeTypeDefinition declaration => declaration.Source.Project.Coverage.Measure(
                [declaration.Source],
                declaration.Source.Locate(declaration.Syntax.Span)
            ),
            ICodeElement { Source: { } source } element => source.Project.Coverage.Measure(
                [source],
                element.Location
            ),
            _ => new(
                0,
                0,
                false,
                Array.AsReadOnly(
                    new[] { CoverageReason.EvidenceNotPrepared, CoverageReason.ZeroCoverableLines }
                )
            ),
        };

    /// <summary>Requires a percentage from zero through one hundred. Missing evidence and zero coverable lines fail.</summary>
    public CoverageRequirement AtLeast(double percentage)
    {
        if (!double.IsFinite(percentage) || percentage < 0 || percentage > 100)
            throw new ArgumentOutOfRangeException(nameof(percentage));
        return new(percentage);
    }
}
