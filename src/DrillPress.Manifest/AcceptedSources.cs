namespace DrillPress.Manifest;

/// <summary>
/// Represents the versioned accepted source state that the CLI resolves outside the rule model,
/// for example from a Git revision or an accepted copy of the source tree.
/// </summary>
/// <param name="FileIdentifier">The file-type identifier checked before payload evaluation.</param>
/// <param name="FormatVersion">The exact accepted-source contract version.</param>
/// <param name="Root">The current-tree directory the accepted state covers; sources outside it count as unchanged.</param>
/// <param name="Files">Accepted C# sources under <paramref name="Root"/>, mapped to their current-tree paths.</param>
public sealed record AcceptedSources(
    string FileIdentifier,
    int FormatVersion,
    string Root,
    AcceptedSourceFile[] Files
)
{
    /// <summary>Identifies files that contain a Drill Press accepted source state.</summary>
    public const string ExpectedFileIdentifier = "drillpress-accepted-sources";

    /// <summary>Identifies the exact accepted-source shape supported by this build.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>The exact package version that produced this state.</summary>
    [System.Text.Json.Serialization.JsonRequired]
    public string ProductVersion { get; init; } = ComponentVersion.Current;

    /// <summary>Creates an accepted state using the current envelope identifiers.</summary>
    public static AcceptedSources Create(string root, params AcceptedSourceFile[] files) =>
        new(ExpectedFileIdentifier, CurrentFormatVersion, root, files);
}
