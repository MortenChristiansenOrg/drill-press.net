using System.Text.Json;

namespace DrillPress.Manifest;

/// <summary>
/// Represents the versioned envelope exchanged between BuildHost and a compiled rule bundle.
/// </summary>
/// <param name="Magic">The file-type identifier checked before payload evaluation.</param>
/// <param name="FormatVersion">The exact snapshot contract version.</param>
/// <param name="Projects">The evaluated project compilations carried by the snapshot.</param>
public sealed record CompilationSnapshot(
    string Magic,
    int FormatVersion,
    ProjectSnapshot[] Projects)
{
    /// <summary>Identifies files that contain a Drill Press compilation snapshot.</summary>
    public const string ExpectedMagic = "drillpress-compilation";

    /// <summary>Identifies the exact snapshot shape supported by this build.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Creates a snapshot using the current envelope identifiers.</summary>
    public static CompilationSnapshot Create(params ProjectSnapshot[] projects) =>
        new(ExpectedMagic, CurrentFormatVersion, projects);

    /// <summary>Reads and validates a snapshot before exposing its payload.</summary>
    public static async Task<CompilationSnapshot> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var snapshot = await JsonSerializer.DeserializeAsync(
                stream,
                CompilationSnapshotJsonContext.Default.CompilationSnapshot,
                cancellationToken)
            ?? throw new InvalidDataException($"Compilation snapshot '{path}' is empty.");
        snapshot.Validate();
        return snapshot;
    }

    /// <summary>Validates and serializes the snapshot to <paramref name="path"/>.</summary>
    public async Task WriteAsync(string path, CancellationToken cancellationToken = default)
    {
        Validate();
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(
            stream,
            this,
            CompilationSnapshotJsonContext.Default.CompilationSnapshot,
            cancellationToken);
    }

    private void Validate()
    {
        if (!StringComparer.Ordinal.Equals(Magic, ExpectedMagic))
        {
            throw new InvalidDataException("The input is not a Drill Press compilation snapshot.");
        }

        if (FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Compilation snapshot format {FormatVersion} is not supported; expected {CurrentFormatVersion}.");
        }
    }
}
