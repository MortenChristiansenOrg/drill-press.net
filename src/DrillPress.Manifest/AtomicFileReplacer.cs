using System.IO.Abstractions;

namespace DrillPress.Manifest;

internal class AtomicFileReplacer(IFileSystem fileSystem, SourceFilePolicy policy)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly SourceFilePolicy _policy = policy;

    internal virtual async Task PrepareAsync(PreparedSourceFile file, CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using var stream = _fileSystem.FileStream.New(file.TemporaryPath, options);
        file.TemporaryCreated = true;
        await stream.WriteAsync(file.ReplacementBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    internal virtual async Task ReplaceAsync(CompilationSnapshot snapshot, PreparedSourceFile file, CancellationToken cancellationToken)
    {
        var states = _policy.Inspect(snapshot, cancellationToken);
        var state = states[file.Document.FileIdentity];
        if (states.Values.Any(value => value.Identity is null) || !state.IsEditable || state.Identity != file.Identity ||
            !(await _fileSystem.File.ReadAllBytesAsync(file.Path, cancellationToken)).AsSpan().SequenceEqual(file.OriginalBytes))
        {
            throw new IOException("Source identity or bytes changed before replacement.");
        }

        if (!_policy.Inspect(file.TemporaryPath).IsEditable || !(await _fileSystem.File.ReadAllBytesAsync(file.TemporaryPath, cancellationToken)).AsSpan().SequenceEqual(file.ReplacementBytes))
        {
            throw new IOException("Prepared source bytes changed before replacement.");
        }

        if (!OperatingSystem.IsWindows())
        {
            _fileSystem.File.SetUnixFileMode(file.TemporaryPath, _fileSystem.File.GetUnixFileMode(file.Path));
        }

        cancellationToken.ThrowIfCancellationRequested();
        _fileSystem.File.Replace(file.TemporaryPath, file.Path, null);
        file.TemporaryCreated = false;
    }
}
