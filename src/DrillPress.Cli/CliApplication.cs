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
    private readonly FixPlanApplier _fixes;

    /// <summary>Creates the coordinator for local BuildHost and rule-bundle processes.</summary>
    public CliApplication() : this(new FileSystem(), new ChildProcessRunner())
    {
    }

    internal CliApplication(IFileSystem fileSystem, ChildProcessRunner processRunner, FixPlanApplier? fixes = null)
    {
        _fileSystem = fileSystem;
        _processRunner = processRunner;
        _fixes = fixes ?? new FixPlanApplier(fileSystem, new FileIdentityProbe());
    }

    /// <summary>
    /// Executes a public check or single-pass fix command and returns its typed process outcome.
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
                "Usage: drillpress check|fix --build-host <path> --rules <path> <target> [--property Name=Value] [--validate-compilation]");
            return CliExitCode.Failure;
        }

        try
        {
            var temporaryDirectory = _fileSystem.Directory.CreateTempSubdirectory("drillpress-");
            try
            {
                var snapshotPath = _fileSystem.Path.Combine(temporaryDirectory.FullName, "compilation.snapshot.json");
                var evaluation = await EvaluateAsync(options, snapshotPath, standardError, cancellationToken);
                if (options.Command == CliCommand.Fix && evaluation.Result.Edits.Length > 0)
                {
                    var application = await _fixes.ApplyAsync(evaluation.Snapshot, evaluation.Response, cancellationToken);
                    if (application.Outcome != FixApplicationOutcome.Completed)
                    {
                        await WriteRecoveryAsync(standardError, application);
                        return CliExitCode.Failure;
                    }

                    if (application.Changed.Length > 0)
                    {
                        try
                        {
                            evaluation = await EvaluateAsync(options, snapshotPath, standardError, cancellationToken);
                        }
                        catch (Exception exception)
                        {
                            await standardError.WriteLineAsync($"drillpress: Files changed but verification failed: {exception.Message}");
                            await WritePathsAsync(standardError, "changed", application.Changed);
                            return CliExitCode.Failure;
                        }
                    }
                }

                await standardOutput.WriteAsync(new CompactDiagnosticRenderer(_fileSystem).Render(evaluation.Result));
                return evaluation.Result.Findings.Length == 0 ? CliExitCode.Clean : CliExitCode.Findings;
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

    private async Task<RuleEvaluation> EvaluateAsync(CliOptions options, string snapshotPath, TextWriter error, CancellationToken cancellationToken)
    {
        var export = await _processRunner.CaptureAsync(options.BuildHost,
            ["export", options.Target, snapshotPath, .. options.ExportArguments], cancellationToken);
        await error.WriteAsync(export.StandardError);
        if (export.ExitCode != 0)
        {
            throw new IOException($"BuildHost exited {export.ExitCode}.");
        }

        var snapshot = await new CompilationSnapshotFile(_fileSystem).ReadAsync(snapshotPath, cancellationToken);
        var check = await _processRunner.CaptureAsync(options.Rules, ["check", snapshotPath], cancellationToken);
        await error.WriteAsync(check.StandardError);
        if (check.ExitCode is not (0 or 1))
        {
            throw new IOException($"Rule bundle exited {check.ExitCode}.");
        }

        var result = BundleResponseProtocol.Read(check.StandardOutput, snapshot);
        if (check.ExitCode != (result.Findings.Length == 0 ? 0 : 1))
        {
            throw new InvalidDataException("Bundle exit code disagrees with its response.");
        }

        return new(snapshot, result, check.StandardOutput);
    }

    private static async Task WriteRecoveryAsync(TextWriter error, FixApplicationResult result)
    {
        await error.WriteLineAsync($"drillpress: Fix stopped: {result.Error}");
        await WritePathsAsync(error, "changed", result.Changed);
        await WritePathsAsync(error, "failed", result.Failed is { } failed ? [failed] : []);
        await WritePathsAsync(error, "pending", result.Pending);
    }

    private static async Task WritePathsAsync(TextWriter error, string label, IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            await error.WriteLineAsync($"  {label}: {System.Text.Json.JsonEncodedText.Encode(path)}");
        }
    }
}
