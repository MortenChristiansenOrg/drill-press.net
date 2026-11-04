using System.ComponentModel;
using System.Formats.Tar;
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

            foreach (var file in _fileSystem.Directory.EnumerateFiles(directory, "*.cs"))
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
        var start = NearestDirectory(_fileSystem.Path.GetFullPath(target));
        var unresolved = $"Baseline '{revision}' is neither a directory nor a Git revision.";
        var parent = await GitAsync(
            ["-C", start, "rev-parse", "--show-cdup"],
            unresolved,
            cancellationToken
        );
        var root = _fileSystem.Path.TrimEndingDirectorySeparator(
            _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(start, parent))
        );
        var commit = await GitAsync(
            [
                "-C",
                root,
                "rev-parse",
                "--verify",
                "--quiet",
                "--end-of-options",
                revision + "^{commit}",
            ],
            unresolved,
            cancellationToken
        );
        var archive = _fileSystem.Path.Combine(directory, "accepted.tar");
        await GitAsync(
            ["-C", root, "archive", "--format=tar", "-o", archive, commit, "--", ":(glob)**/*.cs"],
            $"Git could not archive the C# sources of baseline '{revision}'.",
            cancellationToken
        );
        var files = new List<AcceptedSourceFile>();
        await using (var stream = _fileSystem.File.OpenRead(archive))
        {
            using var reader = new TarReader(stream);
            while (
                await reader.GetNextEntryAsync(cancellationToken: cancellationToken) is { } entry
            )
            {
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                    continue;
                using var content = new MemoryStream();
                if (entry.DataStream is { } data)
                    await data.CopyToAsync(content, cancellationToken);
                files.Add(Accepted(root, entry.Name, content.ToArray()));
            }
        }

        _fileSystem.File.Delete(archive);
        return AcceptedSources.Create(root, Ordered(files));
    }

    private AcceptedSourceFile Accepted(string currentRoot, string relativePath, byte[] content)
    {
        var path = _fileSystem.Path.GetFullPath(
            _fileSystem.Path.Combine(currentRoot, relativePath)
        );
        return new AcceptedSourceFile(path, content, !_fileSystem.File.Exists(path));
    }

    private async Task<string> GitAsync(
        IReadOnlyList<string> arguments,
        string failure,
        CancellationToken cancellationToken
    )
    {
        ChildProcessResult result;
        try
        {
            result = await runner.CaptureAsync("git", arguments, cancellationToken);
        }
        catch (Win32Exception exception)
        {
            throw new IOException($"{failure} Git could not be started.", exception);
        }

        return result.ExitCode == 0 ? result.StandardOutput.TrimEnd('\r', '\n')
            : string.IsNullOrWhiteSpace(result.StandardError) ? throw new IOException(failure)
            : throw new IOException($"{failure} {result.StandardError.Trim()}");
    }

    private string NearestDirectory(string path)
    {
        while (!_fileSystem.Directory.Exists(path))
            path =
                _fileSystem.Path.GetDirectoryName(path)
                ?? throw new DirectoryNotFoundException($"No directory contains '{path}'.");
        return path;
    }

    private static AcceptedSourceFile[] Ordered(List<AcceptedSourceFile> files) =>
        files.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
}
