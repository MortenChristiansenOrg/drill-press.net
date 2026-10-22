using DrillPress.Manifest;

namespace DrillPress.BuildHost;

/// <summary>MSBuild policy facts of one loaded target-framework context.</summary>
internal sealed record EvaluatedMetadata(
    string TargetFramework,
    bool IsTestProject,
    Dictionary<string, string> Properties,
    string AssetsPath,
    PackageReferenceSnapshot[] Packages,
    string[] SourceRoots,
    string? CompilerAssemblyPath
);
