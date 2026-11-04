using System.ComponentModel;
using System.IO.Abstractions;
using System.Text;

namespace DrillPress.Cli;

/// <summary>
/// Reads the C# blobs of a Git revision as a checkout would write them, applying line-ending
/// and smudge filters but not release-archive attributes such as <c>export-ignore</c>.
/// </summary>
internal sealed class GitRevisionSources(IFileSystem fileSystem, ChildProcessRunner runner)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    /// <summary>Returns the repository root as the target's path spells it, and its C# sources relative to that root.</summary>
    internal async Task<(string Root, List<(string Path, byte[] Content)> Files)> ReadAsync(
        string revision,
        string start,
        string directory,
        CancellationToken cancellationToken
    )
    {
        var unresolved = $"Baseline '{revision}' is neither a directory nor a Git revision.";
        var location = (
            await GitAsync(
                ["-C", start, "rev-parse", "--show-toplevel", "--show-prefix"],
                unresolved,
                cancellationToken
            )
        ).Split('\n');
        var root =
            LogicalRoot(start, location.Length > 1 ? location[1] : "")
            ?? _fileSystem.Path.GetFullPath(location[0]);
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
        var blobs = (
            await GitAsync(
                ["-C", root, "ls-tree", "-r", "-z", "--full-tree", commit],
                $"Git could not list baseline '{revision}'.",
                cancellationToken
            )
        )
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Split('\t', 2))
            .Where(entry =>
                entry is [var metadata, var path]
                && metadata.StartsWith("100", StringComparison.Ordinal)
                && metadata.Contains(" blob ", StringComparison.Ordinal)
                && !path.Contains('\n')
                && AcceptedSourceResolver.IsSource(path)
            )
            .Select(entry => (Object: entry[0].Split(' ')[2], Path: entry[1]))
            .ToArray();
        var files = new List<(string, byte[])>();
        if (blobs.Length == 0)
            return (root, files);

        var batch = _fileSystem.Path.Combine(directory, "accepted.batch");
        try
        {
            ChildProcessResult result;
            await using (var output = _fileSystem.File.Create(batch))
                result = await StartAsync(
                    () =>
                        runner.StreamAsync(
                            "git",
                            ["-C", root, "cat-file", "--batch", "--filters"],
                            // Filters need each blob's path, which follows its object name.
                            string.Concat(blobs.Select(blob => $"{blob.Object} {blob.Path}\n")),
                            output,
                            cancellationToken
                        ),
                    unresolved
                );
            if (result.ExitCode != 0)
                throw new IOException(
                    $"Git could not read baseline '{revision}'. {result.StandardError.Trim()}"
                );
            await using var stream = new BufferedStream(_fileSystem.File.OpenRead(batch));
            foreach (var (_, path) in blobs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var header = ReadLine(stream).Split(' ');
                if (header is not [_, "blob", var size])
                    throw new InvalidDataException(
                        $"Git returned no source for '{path}' in baseline '{revision}'."
                    );
                var content = new byte[
                    int.Parse(size, System.Globalization.CultureInfo.InvariantCulture)
                ];
                stream.ReadExactly(content);
                stream.ReadByte();
                files.Add((path, content));
            }
        }
        finally
        {
            _fileSystem.File.Delete(batch);
        }

        return (root, files);
    }

    // Keeps the target's spelling of the root, so a symlinked checkout still matches captured paths.
    private string? LogicalRoot(string start, string prefix)
    {
        var directory = start;
        foreach (var segment in prefix.Split('/', StringSplitOptions.RemoveEmptyEntries).Reverse())
        {
            if (
                !string.Equals(
                    _fileSystem.Path.GetFileName(directory),
                    segment,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal
                )
            )
                return null;
            directory = _fileSystem.Path.GetDirectoryName(directory);
            if (directory is null)
                return null;
        }

        return directory;
    }

    private async Task<string> GitAsync(
        IReadOnlyList<string> arguments,
        string failure,
        CancellationToken cancellationToken
    )
    {
        var result = await StartAsync(
            () => runner.CaptureAsync("git", arguments, cancellationToken),
            failure
        );
        return result.ExitCode == 0 ? result.StandardOutput.TrimEnd('\r', '\n')
            : string.IsNullOrWhiteSpace(result.StandardError) ? throw new IOException(failure)
            : throw new IOException($"{failure} {result.StandardError.Trim()}");
    }

    private static async Task<ChildProcessResult> StartAsync(
        Func<Task<ChildProcessResult>> run,
        string failure
    )
    {
        try
        {
            return await run();
        }
        catch (Win32Exception exception)
        {
            throw new IOException($"{failure} Git could not be started.", exception);
        }
    }

    private static string ReadLine(Stream stream)
    {
        var line = new List<byte>();
        for (var value = stream.ReadByte(); value is not (-1 or '\n'); value = stream.ReadByte())
            line.Add((byte)value);
        return Encoding.UTF8.GetString(line.ToArray());
    }
}
