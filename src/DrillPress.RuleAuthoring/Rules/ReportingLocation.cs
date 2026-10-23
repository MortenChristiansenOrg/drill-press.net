using System.Runtime.CompilerServices;
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
        var owner = Element(candidate)?.Source;
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
        Element(candidate)?.Source.Project
        ?? Project(candidate)
        ?? throw new ArgumentException(
            $"Candidate type '{typeof(T)}' has no compilation; report at an ICodeElement instead.",
            nameof(candidate)
        );

    // A tuple belongs to the compilation of its first code element, searching nested tuples in order.
    private static ICodeElement? Element(object? candidate) =>
        candidate switch
        {
            ICodeElement element => element,
            ITuple tuple => Enumerable
                .Range(0, tuple.Length)
                .Select(index => Element(tuple[index]))
                .FirstOrDefault(element => element is not null),
            _ => null,
        };

    private static AnalysisProject? Project(object? candidate) =>
        candidate switch
        {
            AnalysisProject project => project,
            ITuple tuple => Enumerable
                .Range(0, tuple.Length)
                .Select(index => Project(tuple[index]))
                .FirstOrDefault(project => project is not null),
            _ => null,
        };

    /// <summary>Whether values of a type can have a compilation: code elements, projects, tuples that can hold one, and open types such as interfaces.</summary>
    internal static bool CanHaveCompilation(Type type) =>
        typeof(ICodeElement).IsAssignableFrom(type)
        || type == typeof(AnalysisProject)
        || (
            typeof(ITuple).IsAssignableFrom(type)
                ? !type.IsGenericType || type.GetGenericArguments().Any(CanHaveCompilation)
                : !type.IsValueType && !type.IsSealed
        );

    /// <summary>Whether values of a type can have their own location: code elements, projects, and open types such as interfaces.</summary>
    internal static bool CanHaveLocation(Type type) =>
        typeof(ICodeElement).IsAssignableFrom(type)
        || type == typeof(AnalysisProject)
        || typeof(ITuple).IsAssignableFrom(type)
        || !type.IsValueType && !type.IsSealed;

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
