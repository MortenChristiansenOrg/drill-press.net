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
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (json.RootElement.ValueKind == JsonValueKind.Null)
        {
            throw new InvalidDataException($"Compilation snapshot '{path}' is empty.");
        }

        ValidateHeader(json.RootElement);
        var snapshot = json.RootElement.Deserialize(CompilationSnapshotJsonContext.Default.CompilationSnapshot)
            ?? throw new InvalidDataException("Missing compilation snapshot.");
        SnapshotValidation.Validate(snapshot);
        return snapshot;
    }

    /// <summary>Writes the current JSON contract to a sibling file before replacing the destination.</summary>
    public async Task WriteAsync(
        string path, CompilationSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        SnapshotValidation.Validate(snapshot);
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

    private static void ValidateHeader(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The input is not a Drill Press compilation snapshot.");
        }

        var properties = root.EnumerateObject().ToArray();
        if (properties.Length < 2 || properties[0].Name != "fileIdentifier" || properties[1].Name != "formatVersion" ||
            properties[0].Value.ValueKind != JsonValueKind.String || !properties[1].Value.TryGetInt32(out var version))
        {
            throw new InvalidDataException("Invalid snapshot header. Use matching Drill Press components.");
        }

        SnapshotValidation.ValidateEnvelope(properties[0].Value.GetString()!, version);
    }
}
