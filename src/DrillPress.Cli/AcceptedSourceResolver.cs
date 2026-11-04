using System.IO.Abstractions;
using DrillPress.Manifest;

namespace DrillPress.Cli;

/// <summary>
/// Resolves a <c>--baseline</c> value into accepted C# sources. An existing directory is an
/// accepted copy of the current directory; any other value is a Git revision of the target's
/// repository. Version control stays here, outside the rule model.
/// </summary>
internal sealed class AcceptedSourceResolver(IFileSystem fileSystem, ChildProcessRunner runner)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    internal async Task<string> WriteAsync(
        string baseline,
        string target,
        string directory,
        CancellationToken cancellationToken
    )
    {
        var accepted = _fileSystem.Directory.Exists(baseline)
            ? ReadDirectory(
                _fileSystem.Path.GetFullPath(baseline),
                _fileSystem.Directory.GetCurrentDirectory(),
                cancellationToken
            )
            : await ReadRevisionAsync(baseline, target, directory, cancellationToken);
        var path = _fileSystem.Path.Combine(directory, "accepted.sources.json");
        await new AcceptedSourcesFile(_fileSystem).WriteAsync(path, accepted, cancellationToken);
        return path;
    }

    private AcceptedSources ReadDirectory(
        string acceptedRoot,
        string currentRoot,
        CancellationToken cancellationToken
    )
    {
        var files = new List<AcceptedSourceFile>();
        var pending = new Stack<string>([acceptedRoot]);
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var child in _fileSystem.Directory.EnumerateDirectories(directory))
            {
                // Build output, hidden folders and linked folders are not part of the accepted copy.
                if (
                    _fileSystem.Path.GetFileName(child) is not ("bin" or "obj") and not ['.', ..]
                    && !_fileSystem.File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint)
                )
                    pending.Push(child);
            }

            foreach (var file in _fileSystem.Directory.EnumerateFiles(directory).Where(IsSource))
            {
                files.Add(
                    Accepted(
                        currentRoot,
                        _fileSystem.Path.GetRelativePath(acceptedRoot, file),
                        _fileSystem.File.ReadAllBytes(file)
                    )
                );
            }
        }

        return AcceptedSources.Create(currentRoot, Ordered(files));
    }

    private async Task<AcceptedSources> ReadRevisionAsync(
        string revision,
        string target,
        string directory,
        CancellationToken cancellationToken
    )
    {
        var (root, files) = await new GitRevisionSources(_fileSystem, runner).ReadAsync(
            revision,
            NearestDirectory(_fileSystem.Path.GetFullPath(target)),
            directory,
            cancellationToken
        );
        return AcceptedSources.Create(
            root,
            Ordered(files.Select(file => Accepted(root, file.Path, file.Content)))
        );
    }

    /// <summary>Matches C# sources the way targets do, ignoring the extension's case.</summary>
    internal static bool IsSource(string path) =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    private AcceptedSourceFile Accepted(string currentRoot, string relativePath, byte[] content)
    {
        var path = _fileSystem.Path.GetFullPath(
            _fileSystem.Path.Combine(currentRoot, relativePath)
        );
        if (
            !path.StartsWith(
                _fileSystem.Path.TrimEndingDirectorySeparator(currentRoot)
                    + _fileSystem.Path.DirectorySeparatorChar,
                StringComparison.Ordinal
            )
        )
            throw new InvalidDataException(
                $"Accepted source '{relativePath}' is outside its root."
            );
        return new AcceptedSourceFile(path, content, !_fileSystem.File.Exists(path));
    }

    private string NearestDirectory(string path)
    {
        while (!_fileSystem.Directory.Exists(path))
            path =
                _fileSystem.Path.GetDirectoryName(path)
                ?? throw new DirectoryNotFoundException($"No directory contains '{path}'.");
        return path;
    }

    private static AcceptedSourceFile[] Ordered(IEnumerable<AcceptedSourceFile> files) =>
        files.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
}
