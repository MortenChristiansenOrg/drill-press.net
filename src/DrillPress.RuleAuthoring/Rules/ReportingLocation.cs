using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

internal sealed record ReportTarget(SourceLocation Location, AnalysisSource Source);

internal static class ReportingLocation
{
    internal static ReportTarget Default<T>(T candidate) =>
        candidate switch
        {
            ICodeElement element => new(element.Location, element.Source),
            AnalysisProject project => project
                .Sources.Where(source => !source.Document.IsGenerated)
                .Select(source => new ReportTarget(source.Locate(new(0, 0)), source))
                .FirstOrDefault()
                ?? throw new InvalidOperationException(
                    $"Project '{project.Name}' has no ordinary source file to report at."
                ),
            _ => throw new InvalidOperationException(
                $"Candidate type '{typeof(T)}' has no source location; choose one with ReportAt(...)."
            ),
        };

    internal static ReportTarget Element<T>(T candidate, ICodeElement part)
    {
        if (candidate is ICodeElement owner && part.Source.Project != owner.Source.Project)
            throw new ArgumentException(
                "The reported element belongs to another compilation context.",
                nameof(part)
            );
        return new(Validate(part.Source.Project, part.Location), part.Source);
    }

    internal static ReportTarget Span<T>(T candidate, SyntaxTree tree, TextSpan span)
    {
        var project = Owner(candidate);
        var member =
            project.Sources.FirstOrDefault(source => source.Tree == tree)
            ?? throw new ArgumentException(
                "The selected syntax must belong to the candidate's original compilation.",
                "syntax"
            );
        return new(member.Locate(span), member);
    }

    internal static ReportTarget Physical<T>(T candidate, SourceLocation location)
    {
        var project = Owner(candidate);
        var owner = (candidate as ICodeElement)?.Source;
        var source =
            owner?.Document.Path == location.FilePath
                ? owner
                : project.Sources.FirstOrDefault(source =>
                    source.Document.Path == location.FilePath
                );
        return new(
            Validate(project, location),
            source
                ?? throw new ArgumentException(
                    "The reporting span must belong to an original document in the candidate's compilation.",
                    nameof(location)
                )
        );
    }

    private static AnalysisProject Owner<T>(T candidate) =>
        candidate switch
        {
            ICodeElement element => element.Source.Project,
            AnalysisProject project => project,
            _ => throw new ArgumentException(
                $"Candidate type '{typeof(T)}' has no compilation; report at an ICodeElement instead.",
                nameof(candidate)
            ),
        };

    private static SourceLocation Validate(AnalysisProject project, SourceLocation location)
    {
        var target = project.Sources.FirstOrDefault(candidate =>
            candidate.Document.Path == location.FilePath
        );
        if (
            target is null
            || location.Start < 0
            || location.Length < 0
            || (long)location.Start + location.Length > target.Tree.Length
        )
            throw new ArgumentException(
                "The reporting span must belong to an original document in the candidate's compilation.",
                nameof(location)
            );
        return location;
    }
}
