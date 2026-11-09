using System.IO.Abstractions;

namespace DrillPress.Cli;

/// <summary>
/// Turns a rules source into the rule bundle to run. A compiled managed DLL or native executable
/// runs as given. A rules project, or a directory holding exactly one, is restored and built out
/// of process in Release first, and its build output runs instead. Relative paths resolve against
/// the current directory. Target restore stays the user's responsibility.
/// </summary>
internal sealed class RulesSourceResolver(IFileSystem fileSystem, ChildProcessRunner runner)
{
    private const string Configuration = "Release";

    private readonly IFileSystem _fileSystem = fileSystem;

    /// <summary>
    /// Returns the bundle to run for <paramref name="rules"/>. Missing or ambiguous projects fail
    /// before anything starts, so callers can check the returned task before starting other work.
    /// </summary>
    internal async Task<string> ResolveAsync(string rules, CancellationToken cancellationToken)
    {
        var project = FindProject(rules);
        return project is null ? rules : await BuildAsync(rules, project, cancellationToken);
    }

    private string? FindProject(string rules)
    {
        var path = _fileSystem.Path.GetFullPath(rules);
        if (_fileSystem.Directory.Exists(path))
        {
            var projects = _fileSystem.Directory.GetFiles(path).Where(IsProject).ToArray();
            return projects.Length == 1
                ? projects[0]
                : throw new InvalidOperationException(
                    $"Rules directory '{rules}' contains {projects.Length} .csproj files; specify one."
                );
        }

        if (!IsProject(path))
            return null;
        return _fileSystem.File.Exists(path)
            ? path
            : throw new FileNotFoundException($"Rules project '{rules}' was not found.", path);
    }

    private async Task<string> BuildAsync(
        string rules,
        string project,
        CancellationToken cancellationToken
    )
    {
        // With -getProperty, MSBuild prints only the property on stdout and diagnostics on stderr.
        var build = await runner.CaptureAsync(
            "dotnet",
            [
                "build",
                project,
                "--configuration",
                Configuration,
                "-nologo",
                "-t:Build",
                "-getProperty:TargetPath",
            ],
            cancellationToken
        );
        if (build.ExitCode != 0)
            throw new IOException(
                $"Rules project '{rules}' failed to build:{Environment.NewLine}{Errors(project, build)}"
            );
        var output = build.StandardOutput.Trim();
        return output.Length > 0 && !output.Contains('\n')
            ? output
            : throw new InvalidOperationException(
                $"Rules project '{rules}' has no single build output; set one TargetFramework."
            );
    }

    // Keeps only error lines, without the project suffix and with paths under the current directory made relative.
    private string Errors(string project, ChildProcessResult build)
    {
        var currentDirectory = _fileSystem.Path.TrimEndingDirectorySeparator(
            _fileSystem.Directory.GetCurrentDirectory()
        );
        var prefix = currentDirectory + _fileSystem.Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var lines = (build.StandardError + "\n" + build.StandardOutput).Split(
            '\n',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
        );
        var errors = lines
            .Where(line => line.Contains(": error ", StringComparison.Ordinal))
            .ToArray();
        var report = errors
            .Select(line =>
                line.EndsWith(']')
                && line.LastIndexOf(" [", StringComparison.Ordinal) is > 0 and var suffix
                    ? line[..suffix]
                    : line
            )
            .Select(line => line.StartsWith(prefix, comparison) ? line[prefix.Length..] : line)
            .Distinct()
            .Select(line => "  " + line)
            .ToArray();
        return report.Length > 0
            ? string.Join(Environment.NewLine, report)
            : $"  dotnet build exited {build.ExitCode}; run 'dotnet build \"{project}\"' for details.";
    }

    private static bool IsProject(string path) =>
        path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
}
