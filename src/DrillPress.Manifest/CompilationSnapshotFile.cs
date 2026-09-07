using System.IO.Abstractions;
using System.Text.Json;

namespace DrillPress.Manifest;

/// <summary>Persists the internal snapshot contract through the internal filesystem boundary.</summary>
public sealed class CompilationSnapshotFile
{
    private readonly IFileSystem _fileSystem;

    /// <summary>Creates snapshot storage on the local filesystem.</summary>
    public CompilationSnapshotFile() : this(new FileSystem())
    {
    }

    internal CompilationSnapshotFile(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>Reads and validates the versioned envelope before exposing compiler inputs.</summary>
    public async Task<CompilationSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = _fileSystem.File.OpenRead(path);
        var snapshot = await JsonSerializer.DeserializeAsync(
            stream, CompilationSnapshotJsonContext.Default.CompilationSnapshot, cancellationToken)
            ?? throw new InvalidDataException($"Compilation snapshot '{path}' is empty.");
        Validate(snapshot);
        return snapshot;
    }

    /// <summary>Writes the current JSON contract to a sibling file before replacing the destination.</summary>
    public async Task WriteAsync(
        string path, CompilationSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Validate(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        var destinationPath = _fileSystem.Path.GetFullPath(path);
        var temporaryPath = destinationPath + $".{Guid.NewGuid():N}.tmp";
        var stream = _fileSystem.File.Open(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            await using (stream)
            {
                await JsonSerializer.SerializeAsync(
                    stream, snapshot, CompilationSnapshotJsonContext.Default.CompilationSnapshot, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            _fileSystem.File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            _fileSystem.File.Delete(temporaryPath);
        }
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
