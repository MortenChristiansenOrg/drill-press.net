using System.IO.Abstractions;
using DrillPress.Manifest;

namespace DrillPress.Cli;

/// <summary>
/// Coordinates target export and compiled rule execution without loading MSBuild,
/// Roslyn, or rule assemblies into the CLI process.
/// </summary>
public sealed class CliApplication
{
    private readonly IFileSystem _fileSystem;
    private readonly ChildProcessRunner _processRunner;

    /// <summary>Creates the coordinator for local BuildHost and rule-bundle processes.</summary>
    public CliApplication() : this(new FileSystem(), new ChildProcessRunner())
    {
    }

    internal CliApplication(IFileSystem fileSystem, ChildProcessRunner processRunner)
    {
        _fileSystem = fileSystem;
        _processRunner = processRunner;
    }

    /// <summary>
    /// Executes the public check command and returns its typed process outcome.
    /// </summary>
    public async Task<CliExitCode> RunAsync(
        string[] args,
        TextWriter? standardError = null,
        CancellationToken cancellationToken = default,
        TextWriter? standardOutput = null)
    {
        standardError ??= Console.Error;
        standardOutput ??= Console.Out;
        if (!CliOptions.TryParse(args, out var options))
        {
            await standardError.WriteLineAsync(
                "Usage: drillpress check --build-host <path> --rules <path> <project.csproj>");
            return CliExitCode.Failure;
        }

        try
        {
            var temporaryDirectory = _fileSystem.Directory.CreateTempSubdirectory("drillpress-");
            try
            {
                var snapshotPath = _fileSystem.Path.Combine(temporaryDirectory.FullName, "compilation.snapshot.json");
                var buildHostResult = await _processRunner.CaptureAsync(
                    options.BuildHost,
                    ["export", options.Target, snapshotPath],
                    cancellationToken);
                await standardError.WriteAsync(buildHostResult.StandardError);
                if (buildHostResult.ExitCode != (int)CliExitCode.Clean)
                {
                    return CliExitCode.Failure;
                }

                var snapshot = await new CompilationSnapshotFile(_fileSystem).ReadAsync(snapshotPath, cancellationToken);
                var ruleResult = await _processRunner.CaptureAsync(options.Rules, ["check", snapshotPath], cancellationToken);
                await standardError.WriteAsync(ruleResult.StandardError);
                if (ruleResult.ExitCode is not (0 or 1))
                {
                    return CliExitCode.Failure;
                }

                var result = BundleResponseProtocol.Read(ruleResult.StandardOutput, snapshot);
                var expectedExit = result.Findings.Length == 0 ? CliExitCode.Clean : CliExitCode.Findings;
                if (ruleResult.ExitCode != (int)expectedExit)
                {
                    throw new InvalidDataException("Bundle exit code disagrees with its response.");
                }

                await standardOutput.WriteAsync(new CompactDiagnosticRenderer(_fileSystem).Render(result));
                return expectedExit;
            }
            finally
            {
                temporaryDirectory.Delete(recursive: true);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await standardError.WriteLineAsync($"drillpress: {exception.Message}");
            return CliExitCode.Failure;
        }
    }

    private sealed record CliOptions(string BuildHost, string Rules, string Target)
    {
        public static bool TryParse(string[] args, out CliOptions options)
        {
            options = null!;
            if (args.Length < 6 || args[0] != "check")
            {
                return false;
            }

            string? buildHost = null;
            string? rules = null;
            string? target = null;
            for (var index = 1; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--build-host" when buildHost is null && index + 1 < args.Length:
                        buildHost = args[++index];
                        break;
                    case "--rules" when rules is null && index + 1 < args.Length:
                        rules = args[++index];
                        break;
                    default:
                        if (target is not null || args[index].StartsWith('-'))
                        {
                            return false;
                        }

                        target = args[index];
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(buildHost) ||
                string.IsNullOrWhiteSpace(rules) ||
                string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            options = new CliOptions(buildHost, rules, target);
            return true;
        }
    }
}
