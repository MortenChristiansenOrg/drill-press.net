using DrillPress.Queries;

namespace DrillPress.Projects;

/// <summary>Readable project-policy selections over captured, evaluated .NET project facts.</summary>
public static class ProjectFacts
{
    /// <summary>Restricts files to contexts classified as test projects by BuildHost.</summary>
    public static CodeQuery<CodeFile> TestFiles { get; } = Sources.Files.Where(new(file => file.Source.Project.Snapshot.IsTestProject));

    /// <summary>Restricts files to contexts not classified as test projects.</summary>
    public static CodeQuery<CodeFile> ProductionFiles { get; } = Sources.Files.Where(new(file => !file.Source.Project.Snapshot.IsTestProject));

    /// <summary>Tests direct evaluated NuGet references using NuGet's case-insensitive package identity.</summary>
    public static bool ReferencesPackage(this AnalysisProject project, string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        return project.Snapshot.Packages.Any(package => string.Equals(package.Id, packageId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Defines a repository-specific project role in ordinary typed C#.</summary>
    public static CodeQuery<AnalysisProject> WithRole(Func<AnalysisProject, bool> role) => Sources.Projects.Where(new(role));
}
