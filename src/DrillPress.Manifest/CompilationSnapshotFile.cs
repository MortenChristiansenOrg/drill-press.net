using System.IO.Abstractions;
using System.Text.Json;

namespace DrillPress.Manifest;

/// <summary>Persists the internal snapshot contract through the caller's filesystem.</summary>
/// <param name="fileSystem">The filesystem containing the snapshot files.</param>
public sealed class CompilationSnapshotFile(IFileSystem fileSystem)
{
    /// <summary>Reads and validates the versioned envelope before exposing compiler inputs.</summary>
    public async Task<CompilationSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = fileSystem.File.OpenRead(path);
        var snapshot = await JsonSerializer.DeserializeAsync(
            stream, CompilationSnapshotJsonContext.Default.CompilationSnapshot, cancellationToken)
            ?? throw new InvalidDataException($"Compilation snapshot '{path}' is empty.");
        Validate(snapshot);
        return snapshot;
    }

    /// <summary>Validates before opening the destination, then writes the current JSON contract.</summary>
    public async Task WriteAsync(
        string path, CompilationSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Validate(snapshot);
        await using var stream = fileSystem.File.Create(path);
        await JsonSerializer.SerializeAsync(
            stream, snapshot, CompilationSnapshotJsonContext.Default.CompilationSnapshot, cancellationToken);
    }

    private static void Validate(CompilationSnapshot snapshot)
    {
        if (snapshot.FileIdentifier != CompilationSnapshot.ExpectedFileIdentifier)
        {
            throw new InvalidDataException("The input is not a Drill Press compilation snapshot.");
        }

        if (snapshot.FormatVersion != CompilationSnapshot.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Compilation snapshot format {snapshot.FormatVersion} is not supported; expected {CompilationSnapshot.CurrentFormatVersion}.");
        }

        // JSON input can omit non-nullable constructor parameters.
        if (snapshot.Projects is null)
        {
            throw new InvalidDataException("Compilation snapshot must contain a projects array.");
        }
    }
}
