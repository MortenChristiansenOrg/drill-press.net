using System.IO.Abstractions;

namespace DrillPress.Benchmarks;

public sealed class DisposableRepository : IDisposable
{
    private readonly IFileSystem _fileSystem;
    private readonly IDirectoryInfo _directory;

    public DisposableRepository(IFileSystem fileSystem, string source, CancellationToken cancellationToken)
    {
        _fileSystem = fileSystem;
        _directory = fileSystem.Directory.CreateTempSubdirectory("drillpress-performance-");
        try
        {
            Copy(fileSystem.DirectoryInfo.New(source), _directory, cancellationToken);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public string Root => _directory.FullName;

    public void Dispose()
    {
        ClearReadOnly(_directory);
        _directory.Delete(true);
    }

    private static void ClearReadOnly(IDirectoryInfo directory)
    {
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if (entry is IDirectoryInfo child && (entry.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                ClearReadOnly(child);
            }

            if ((entry.Attributes & FileAttributes.ReadOnly) != 0)
            {
                entry.Attributes &= ~FileAttributes.ReadOnly;
            }
        }
    }

    private void Copy(IDirectoryInfo source, IDirectoryInfo destination, CancellationToken cancellationToken)
    {
        foreach (var entry in source.EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"Cannot reproduce linked benchmark input safely: {entry.FullName}");
            }

            var target = _fileSystem.Path.Combine(destination.FullName, entry.Name);
            if (entry is IDirectoryInfo directory)
            {
                Copy(directory, _fileSystem.Directory.CreateDirectory(target), cancellationToken);
            }
            else
            {
                _fileSystem.File.Copy(entry.FullName, target);
            }
        }
    }
}
