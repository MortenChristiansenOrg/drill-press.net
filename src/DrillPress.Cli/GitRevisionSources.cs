using System.ComponentModel;
using System.IO.Abstractions;
using System.Text;

namespace DrillPress.Cli;

/// <summary>
/// Reads the raw C# blobs of a Git revision, unaffected by release-archive attributes such as
/// <c>export-ignore</c>. Checkout filters are not applied; the projection matches line endings.
/// </summary>
internal sealed class GitRevisionSources(IFileSystem fileSystem, ChildProcessRunner runner)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    /// <summary>
    /// Returns the repository root as the first target's path spells it, and its C# sources relative to that root.
    /// Every start directory must belong to that repository, because one revision describes one repository.
    /// </summary>
    internal async Task<(string Root, List<(string Path, byte[] Content)> Files)> ReadAsync(
        string revision,
        IReadOnlyList<string> starts,
        string directory,
        CancellationToken cancellationToken
    )
    {
        var unresolved = $"Baseline '{revision}' is neither a directory nor a Git revision.";
        var start = starts[0];
        var location = (
            await GitAsync(
                ["-C", start, "rev-parse", "--show-toplevel", "--show-prefix"],
                unresolved,
                cancellationToken
            )
        ).Split('\n');
        var topLevel = _fileSystem.Path.GetFullPath(location[0]);
        var root = await RootAsync(
            start,
            topLevel,
            location.Length > 1 ? location[1] : "",
            unresolved,
            cancellationToken
        );
        var others = await Task.WhenAll(
            starts
                .Skip(1)
                .Select(other =>
                    GitAsync(
                        ["-C", other, "rev-parse", "--show-toplevel"],
                        unresolved,
                        cancellationToken
                    )
                )
        );
        for (var index = 0; index < others.Length; index++)
        {
            var otherTopLevel = _fileSystem.Path.GetFullPath(others[index]);
            if (!string.Equals(otherTopLevel, topLevel, StringComparison.Ordinal))
                throw new IOException(
                    $"Baseline '{revision}' needs every target in one Git repository, but '{starts[index + 1]}' is in '{otherTopLevel}' and '{start}' in '{topLevel}'."
                );
        }
        // Accepted paths take the root's spelling, so a target that reaches the checkout through a link would match none.
        var prefix =
            _fileSystem.Path.TrimEndingDirectorySeparator(root)
            + _fileSystem.Path.DirectorySeparatorChar;
        if (
            starts
                .Skip(1)
                .FirstOrDefault(other =>
                    !(other + _fileSystem.Path.DirectorySeparatorChar).StartsWith(
                        prefix,
                        OperatingSystem.IsWindows()
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal
                    )
                ) is
            { } outside
        )
            throw new IOException(
                $"Baseline '{revision}' needs every target under '{root}', the root '{start}' names; '{outside}' reaches the repository by another path."
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
                            // Raw blobs: with --filters, Git 2.43 headers give the unfiltered size.
                            ["-C", root, "cat-file", "--batch"],
                            string.Concat(blobs.Select(blob => blob.Object + "\n")),
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
                if (stream.ReadByte() != '\n')
                    throw new InvalidDataException(
                        $"Git returned malformed source for '{path}' in baseline '{revision}'."
                    );
                files.Add((path, content));
            }
            if (stream.ReadByte() != -1)
                throw new InvalidDataException(
                    $"Git returned more sources than requested for baseline '{revision}'."
                );
        }
        finally
        {
            _fileSystem.File.Delete(batch);
        }

        return (root, files);
    }

    // Keeps the target's spelling of the root, so a symlinked checkout still matches captured
    // paths, but only when Git confirms that spelling names the same checkout.
    private async Task<string> RootAsync(
        string start,
        string topLevel,
        string prefix,
        string failure,
        CancellationToken cancellationToken
    )
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
                ) || _fileSystem.Path.GetDirectoryName(directory) is not { } parent
            )
                return topLevel;
            directory = parent;
        }

        if (string.Equals(directory, topLevel, StringComparison.Ordinal))
            return topLevel;
        var confirmed = await StartAsync(
            () =>
                runner.CaptureAsync(
                    "git",
                    ["-C", directory, "rev-parse", "--show-toplevel"],
                    cancellationToken
                ),
            failure
        );
        return
            confirmed.ExitCode == 0
            && string.Equals(
                _fileSystem.Path.GetFullPath(confirmed.StandardOutput.TrimEnd('\r', '\n')),
                topLevel,
                StringComparison.Ordinal
            )
            ? directory
            : topLevel;
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
