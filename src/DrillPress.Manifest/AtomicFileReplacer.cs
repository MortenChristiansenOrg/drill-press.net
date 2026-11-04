using System.IO.Abstractions;

namespace DrillPress.Manifest;

internal class AtomicFileReplacer(IFileSystem fileSystem, SourceFilePolicy policy)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly SourceFilePolicy _policy = policy;

    internal virtual async Task PrepareAsync(
        PreparedSourceFile file,
        CancellationToken cancellationToken
    )
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using var stream = _fileSystem.FileStream.New(file.TemporaryPath, options);
        file.TemporaryCreated = true;
        await stream.WriteAsync(file.ReplacementBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    internal virtual async Task ReplaceAsync(
        CompilationSnapshot snapshot,
        PreparedSourceFile file,
        CancellationToken cancellationToken
    )
    {
        var states = _policy.Inspect(snapshot, cancellationToken);
        var state = states[file.Document.FileIdentity];
        if (
            states.Values.Any(value => value.Identity is null)
            || !state.IsEditable
            || state.Identity != file.Identity
            || !(await _fileSystem.File.ReadAllBytesAsync(file.Path, cancellationToken))
                .AsSpan()
                .SequenceEqual(file.OriginalBytes)
        )
        {
            throw new IOException("Source identity or bytes changed before replacement.");
        }

        if (
            !_policy.Inspect(file.TemporaryPath).IsEditable
            || !(await _fileSystem.File.ReadAllBytesAsync(file.TemporaryPath, cancellationToken))
                .AsSpan()
                .SequenceEqual(file.ReplacementBytes)
        )
        {
            throw new IOException("Prepared source bytes changed before replacement.");
        }

        if (!OperatingSystem.IsWindows())
        {
            _fileSystem.File.SetUnixFileMode(
                file.TemporaryPath,
                _fileSystem.File.GetUnixFileMode(file.Path)
            );
        }

        cancellationToken.ThrowIfCancellationRequested();
        _fileSystem.File.Replace(file.TemporaryPath, file.Path, null);
        file.TemporaryCreated = false;
    }

    /// <summary>
    /// Rejects a destination that exists as another file or directory, a folder on the way that is a symbolic
    /// link, a reparse point or a file, and a folder below a project directory that holds a project file of
    /// its own, whose default glob would then also compile the moved file.
    /// </summary>
    internal virtual void CheckDestination(PreparedSourceFile file)
    {
        var destination = file.Destination!;
        if (_fileSystem.Directory.Exists(destination))
            throw new IOException($"Cannot move to '{destination}': a directory has that path.");
        if (
            _fileSystem.File.Exists(destination)
            && (
                _policy.Inspect(destination).Identity?.Key is not { } existing
                || existing != _policy.Inspect(file.Path).Identity?.Key
            )
        )
            throw new IOException($"Cannot move to '{destination}': the file already exists.");
        var directory = _fileSystem.Path.GetDirectoryName(destination);
        while (
            directory is not null
            && file.ProjectDirectories.Any(project =>
                directory.StartsWith(
                    project.TrimEnd(_fileSystem.Path.DirectorySeparatorChar)
                        + _fileSystem.Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
        {
            if (_fileSystem.File.Exists(directory))
                throw new IOException($"Cannot move to '{destination}': '{directory}' is a file.");
            if (_fileSystem.Directory.Exists(directory))
            {
                if ((_fileSystem.File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException(
                        $"Cannot move to '{destination}': '{directory}' is a link."
                    );
                if (_fileSystem.Directory.EnumerateFiles(directory, "*.*proj").Any())
                    throw new IOException(
                        $"Cannot move to '{destination}': '{directory}' contains another project."
                    );
            }
            directory = _fileSystem.Path.GetDirectoryName(directory);
        }
    }

    /// <summary>Renames the file, after any content replacement, once its bytes and the destination are verified again.</summary>
    internal virtual Task MoveAsync(
        CompilationSnapshot snapshot,
        PreparedSourceFile file,
        CancellationToken cancellationToken
    ) => MoveVerifiedAsync(snapshot, file, cancellationToken);

    private async Task MoveVerifiedAsync(
        CompilationSnapshot snapshot,
        PreparedSourceFile file,
        CancellationToken cancellationToken
    )
    {
        var states = _policy.Inspect(snapshot, cancellationToken);
        var state = states[file.Document.FileIdentity];
        var expected = file.ChangesContent ? file.ReplacementBytes : file.OriginalBytes;
        if (
            states.Values.Any(value => value.Identity is null)
            || !state.IsEditable
            || !file.ChangesContent && state.Identity != file.Identity
            || !(await _fileSystem.File.ReadAllBytesAsync(file.Path, cancellationToken))
                .AsSpan()
                .SequenceEqual(expected)
        )
        {
            throw new IOException("Source identity or bytes changed before the move.");
        }

        CheckDestination(file);
        _fileSystem.Directory.CreateDirectory(
            _fileSystem.Path.GetDirectoryName(file.Destination!)!
        );
        cancellationToken.ThrowIfCancellationRequested();
        _fileSystem.File.Move(file.Path, file.Destination!);
    }
}
