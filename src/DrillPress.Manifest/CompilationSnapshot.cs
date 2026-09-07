namespace DrillPress.Manifest;

/// <summary>
/// Represents the versioned envelope exchanged between BuildHost and a compiled rule bundle.
/// </summary>
/// <param name="FileIdentifier">The file-type identifier checked before payload evaluation.</param>
/// <param name="FormatVersion">The exact snapshot contract version.</param>
/// <param name="Projects">The evaluated project compilations carried by the snapshot.</param>
public sealed record CompilationSnapshot(
    string FileIdentifier,
    int FormatVersion,
    ProjectSnapshot[] Projects)
{
    /// <summary>Identifies files that contain a Drill Press compilation snapshot.</summary>
    public const string ExpectedFileIdentifier = "drillpress-compilation";

    /// <summary>Identifies the exact snapshot shape supported by this build.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Creates a snapshot using the current envelope identifiers.</summary>
    public static CompilationSnapshot Create(params ProjectSnapshot[] projects) =>
        new(ExpectedFileIdentifier, CurrentFormatVersion, projects);
}
