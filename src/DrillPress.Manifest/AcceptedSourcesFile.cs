using System.IO.Abstractions;
using System.Text.Json;

namespace DrillPress.Manifest;

/// <summary>Persists the accepted-source contract exchanged between the CLI and a rule bundle.</summary>
public sealed class AcceptedSourcesFile
{
    private readonly IFileSystem _fileSystem;

    /// <summary>Creates accepted-source storage on the local filesystem.</summary>
    public AcceptedSourcesFile()
        : this(new FileSystem()) { }

    internal AcceptedSourcesFile(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>Reads and validates the versioned envelope before exposing accepted sources.</summary>
    public async Task<AcceptedSources> ReadAsync(
        string path,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = _fileSystem.File.OpenRead(path);
        var accepted =
            await JsonSerializer.DeserializeAsync(
                stream,
                CompilationSnapshotJsonContext.Default.AcceptedSources,
                cancellationToken
            ) ?? throw new InvalidDataException($"Accepted sources '{path}' are empty.");
        Validate(accepted);
        return accepted;
    }

    /// <summary>Writes a new owner-only file; an existing destination is never replaced.</summary>
    public async Task WriteAsync(
        string path,
        AcceptedSources accepted,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Validate(accepted);
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var stream = _fileSystem.File.Open(path, options);
        await JsonSerializer.SerializeAsync(
            stream,
            accepted,
            CompilationSnapshotJsonContext.Default.AcceptedSources,
            cancellationToken
        );
    }

    private static void Validate(AcceptedSources accepted)
    {
        SnapshotValidation.Require(
            accepted.FileIdentifier == AcceptedSources.ExpectedFileIdentifier,
            "The input is not a Drill Press accepted source state."
        );
        SnapshotValidation.Require(
            accepted.FormatVersion == AcceptedSources.CurrentFormatVersion,
            $"Accepted source format {accepted.FormatVersion} is not supported; expected {AcceptedSources.CurrentFormatVersion}. Use matching Drill Press components."
        );
        ComponentVersion.RequireMatch(accepted.ProductVersion, "Accepted source producer");
        SnapshotValidation.Require(
            !string.IsNullOrWhiteSpace(accepted.Root)
                && accepted.Files.All(file => !string.IsNullOrWhiteSpace(file.Path)),
            "Accepted sources need a root and file paths."
        );
    }
}
