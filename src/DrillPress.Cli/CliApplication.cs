using System.IO.Abstractions;
using DrillPress.Manifest;

namespace DrillPress.Cli;

/// <summary>
/// Coordinates target export and compiled rule execution without loading MSBuild,
/// Roslyn, or rule assemblies into the CLI process.
/// </summary>
public sealed class CliApplication
{
    private const string Usage =
        "Usage: drillpress check|fix|status --rules <path> <target> [--build-host <path>] [--property Name=Value] [--validate-compilation] [--include-referenced-projects] [--profile] [--parallel-queries] [--native-generators] [--worker] [--no-worker] [--no-optimization] [--refresh-coverage] [--skip-coverage] [--explain-coverage] [--explain-fixes] [--show-fix-complexity] [--fix-complexity <levels>]";
    private const string Help = """
        drillpress check|fix|status --rules <path> <target> [options]
        check reports findings; fix applies common-safe edits and reports the recheck;
        status counts violations per project, then per fix complexity.
        Targets: .sln, .slnx, .csproj, directory, .cs file, or quoted C# glob.
        --rules: compiled rule DLL or native executable. --build-host: override the packaged loader.
        --property Name=Value (repeatable)  Override MSBuild properties; restore SDK targets first.
        --include-referenced-projects  Also lint dependencies of a project target (default: selected project only).
        --validate-compilation  Reject compiler errors.  --profile  Write phase timings to stderr.
        --parallel-queries  Prepare pure, thread-safe queries concurrently; reporting and fixes remain ordered.
        --native-generators  Stream source generation through a bundle with a compiled generator catalogue.
        --worker  Reuse private compiler workers; inputs are revalidated and all rules run on each check.
        --no-worker  Start fresh processes. Workers expire after five idle minutes.
        drillpress stop-workers --rules <path> <target>  Stop this target's current workers.
        --explain-coverage  Show typed reasons, context identity and matching source ranges.
        --explain-fixes  Show why each proposed fix without '+' was withheld.
        --refresh-coverage  Rerun tests even when cached coverage inputs match.
        --skip-coverage  Skip coverage rules and their test runs; output lists skipped rules.
        --show-fix-complexity  Include assigned agent fix effort once per rule.
        --fix-complexity <levels>  Select comma-separated trivial,local,complex,architectural,unspecified.
        Complexity selection applies to check, fix, status, and findings exit codes; omitted selects all rules.
        --no-optimization  Use exhaustive queries for comparison.  --help  Show this help.
        --version  Show the package and protocol versions.
        drillpress install-skill [<skills-directory>]  Write this version's rule-authoring agent skill
        to <skills-directory>/drillpress-rules (default .claude/skills).
        Exit codes: 0 no violations (review findings may appear), 1 violations, 2 failure. Fix failures may retain completed writes.
        """;

    private readonly IFileSystem _fileSystem;
    private readonly ChildProcessRunner _processRunner;
    private readonly FixPlanApplier _fixes;
    private readonly SnapshotDirectoryPermissions _permissions;
    private readonly string _applicationDirectory;

    /// <summary>Creates the coordinator for local BuildHost and rule-bundle processes.</summary>
    public CliApplication()
        : this(new FileSystem()) { }

    private CliApplication(IFileSystem fileSystem)
        : this(fileSystem, new WorkerProcessRunner(fileSystem)) { }

    internal CliApplication(
        IFileSystem fileSystem,
        ChildProcessRunner processRunner,
        FixPlanApplier? fixes = null,
        SnapshotDirectoryPermissions? permissions = null,
        string? applicationDirectory = null
    )
    {
        _fileSystem = fileSystem;
        _processRunner = processRunner;
        _fixes = fixes ?? new FixPlanApplier(fileSystem, new FileIdentityProbe());
        _permissions = permissions ?? new SnapshotDirectoryPermissions(fileSystem);
        _applicationDirectory = applicationDirectory ?? AppContext.BaseDirectory;
    }

    /// <summary>
    /// Executes a public check or single-pass fix command, installs the agent skill, or prints help, and returns its typed process outcome.
    /// </summary>
    public async Task<CliExitCode> RunAsync(
        string[] args,
        TextWriter? standardError = null,
        CancellationToken cancellationToken = default,
        TextWriter? standardOutput = null
    )
    {
        standardError ??= Console.Error;
        standardOutput ??= Console.Out;
        if (args is ["--version"])
        {
            await standardOutput.WriteAsync(
                $"drillpress {ComponentVersion.Current} (snapshot {CompilationSnapshot.CurrentFormatVersion}, response {BundleResponseProtocol.CurrentVersion})\n"
            );
            return CliExitCode.Clean;
        }
        if (args is ["--help"] or [("check" or "fix" or "status" or "stop-workers"), "--help"])
        {
            await standardOutput.WriteAsync(Help.ReplaceLineEndings("\n") + "\n");
            return CliExitCode.Clean;
        }
        if (args is ["install-skill", .. var skillArguments] && skillArguments.Length <= 1)
            return await InstallSkillAsync(
                skillArguments is [var directory] ? directory : SkillInstaller.DefaultDirectory,
                standardOutput,
                standardError,
                cancellationToken
            );

        if (!CliOptions.TryParse(args, out var options, out var optionError))
        {
            if (optionError is not null)
                await standardError.WriteLineAsync($"drillpress: {optionError}");
            await standardError.WriteLineAsync(Usage);
            return CliExitCode.Failure;
        }

        var profile = new PipelineProfile(options.Profile, standardError, "cli");
        using var total = profile.Measure("total");
        try
        {
            options = options with { BuildHost = ResolveBuildHost(options.BuildHost) };
            if (options.Command == CliCommand.StopWorkers)
            {
                await _processRunner.CaptureAsync(
                    options.BuildHost!,
                    [CommandWorker.StopCommand, WorkerProcessRunner.Prefix + options.Target],
                    cancellationToken
                );
                await _processRunner.CaptureAsync(
                    options.Rules,
                    [CommandWorker.StopCommand, WorkerProcessRunner.Prefix + options.Target],
                    cancellationToken
                );
                return CliExitCode.Clean;
            }
            var temporaryDirectory = _fileSystem.Directory.CreateTempSubdirectory("drillpress-");
            try
            {
                _permissions.Restrict(temporaryDirectory.FullName);
                var snapshotPath = _fileSystem.Path.Combine(
                    temporaryDirectory.FullName,
                    "compilation.snapshot.json"
                );
                var evaluation = await EvaluateAsync(
                    options,
                    snapshotPath,
                    standardError,
                    profile,
                    cancellationToken
                );
                if (options.Command == CliCommand.Fix && evaluation.Result.Edits.Length > 0)
                {
                    var application = await _fixes.ApplyAsync(
                        evaluation.Snapshot,
                        evaluation.Response,
                        cancellationToken,
                        profile,
                        options.FixComplexities
                    );
                    if (application.Outcome != FixApplicationOutcome.Completed)
                    {
                        await WriteRecoveryAsync(standardError, application);
                        return CliExitCode.Failure;
                    }

                    if (application.Changed.Length > 0)
                    {
                        try
                        {
                            evaluation = await EvaluateAsync(
                                options,
                                snapshotPath,
                                standardError,
                                profile,
                                cancellationToken
                            );
                        }
                        catch (OperationCanceledException)
                        {
                            await WritePathsAsync(standardError, "changed", application.Changed);
                            throw;
                        }
                        catch (Exception exception)
                        {
                            await standardError.WriteLineAsync(
                                $"drillpress: Files changed but verification failed: {exception.Message}"
                            );
                            await WritePathsAsync(standardError, "changed", application.Changed);
                            return CliExitCode.Failure;
                        }
                    }
                }

                using (profile.Measure("rendering"))
                {
                    var renderer = new CompactDiagnosticRenderer(_fileSystem);
                    var text =
                        options.Command == CliCommand.Status
                            ? renderer.RenderStatus(evaluation.Result)
                            : renderer.Render(
                                evaluation.Result,
                                options.ShowFixComplexity,
                                options.ExplainCoverage,
                                options.ExplainFixes
                            );
                    if (profile.Enabled)
                    {
                        profile.Count("public.bytes", System.Text.Encoding.UTF8.GetByteCount(text));
                    }
                    await standardOutput.WriteAsync(text);
                }
                return evaluation.Result.Findings.All(finding =>
                    finding.Disposition == FindingDisposition.Review
                )
                    ? CliExitCode.Clean
                    : CliExitCode.Findings;
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

    private async Task<RuleEvaluation> EvaluateAsync(
        CliOptions options,
        string snapshotPath,
        TextWriter error,
        PipelineProfile profile,
        CancellationToken cancellationToken
    )
    {
        if (
            options.NativeGenerators
            && !options.UseWorker
            && !options.ExportArguments.Contains("--validate-compilation")
        )
        {
            using var generation = profile.Measure("generation.pipeline");
            var pipeline = await new GeneratorProcessPipeline(
                _fileSystem,
                _processRunner
            ).CaptureAsync(options, snapshotPath, RuleFlags(options), cancellationToken);
            await error.WriteAsync(pipeline.Export.StandardError);
            await error.WriteAsync(pipeline.Rules.StandardError);
            var generated = await new CompilationSnapshotFile(_fileSystem).ReadAsync(
                pipeline.SnapshotPath,
                cancellationToken
            );
            return ReadEvaluation(options, generated, pipeline.Rules);
        }
        ChildProcessResult export;
        using (profile.Measure("build-host"))
        {
            export = await _processRunner.CaptureAsync(
                options.BuildHost!,
                [
                    "export",
                    options.Target,
                    snapshotPath,
                    .. options.ExportArguments,
                    .. options.UseWorker
                        ? new[] { WorkerProcessRunner.Prefix + options.Target }
                        : [],
                ],
                cancellationToken
            );
        }
        await error.WriteAsync(export.StandardError);
        if (export.ExitCode != 0)
        {
            throw new IOException($"BuildHost exited {export.ExitCode}.");
        }

        CompilationSnapshot snapshot;
        using (profile.Measure("snapshot.loading"))
        {
            snapshot = await new CompilationSnapshotFile(_fileSystem).ReadAsync(
                snapshotPath,
                cancellationToken
            );
        }

        var ruleArguments = new List<string> { "check", snapshotPath };
        ruleArguments.AddRange(RuleFlags(options));
        ChildProcessResult check;
        using (profile.Measure("rules"))
        {
            check = await _processRunner.CaptureAsync(
                options.Rules,
                ruleArguments,
                cancellationToken
            );
        }
        await error.WriteAsync(check.StandardError);
        return ReadEvaluation(options, snapshot, check);
    }

    private static string[] RuleFlags(CliOptions options)
    {
        var flags = new List<string>();
        if (options.UseWorker)
            flags.Add(WorkerProcessRunner.Prefix + options.Target);
        if (options.Profile)
            flags.Add("--profile");
        if (options.ParallelQueries)
            flags.Add("--parallel-queries");
        if (!options.EnableOptimizations)
            flags.Add("--no-optimization");
        if (options.ExplainCoverage)
            flags.Add("--explain-coverage");
        if (options.RefreshCoverage)
            flags.Add("--refresh-coverage");
        if (options.SkipCoverage)
            flags.Add("--skip-coverage");
        return flags.ToArray();
    }

    private static RuleEvaluation ReadEvaluation(
        CliOptions options,
        CompilationSnapshot snapshot,
        ChildProcessResult check
    )
    {
        if (check.ExitCode is not (0 or 1))
            throw new IOException($"Rule bundle exited {check.ExitCode}.");
        var result = BundleResponseProtocol.Read(
            check.StandardOutput,
            snapshot,
            options.FixComplexities,
            out var completeViolationCount
        );
        if (check.ExitCode != (completeViolationCount == 0 ? 0 : 1))
        {
            throw new InvalidDataException("Bundle exit code disagrees with its response.");
        }

        return new(snapshot, result, check.StandardOutput);
    }

    private string ResolveBuildHost(string? overridePath)
    {
        if (overridePath is not null)
        {
            return overridePath;
        }

        var path = _fileSystem.Path.Combine(
            _applicationDirectory,
            "buildhost",
            "DrillPress.BuildHost.dll"
        );
        if (!_fileSystem.File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Packaged BuildHost was not found at '{path}'. Reinstall DrillPress.Cli {ComponentVersion.Current}, or use --build-host with a matching source build."
            );
        }

        return path;
    }

    private async Task<CliExitCode> InstallSkillAsync(
        string skillsDirectory,
        TextWriter standardOutput,
        TextWriter standardError,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var target = await new SkillInstaller(_fileSystem).InstallAsync(
                skillsDirectory,
                cancellationToken
            );
            await standardOutput.WriteAsync(
                $"{SkillInstaller.Name} {ComponentVersion.Current} installed in {target}\n"
            );
            return CliExitCode.Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await standardError.WriteLineAsync($"drillpress: {exception.Message}");
            return CliExitCode.Failure;
        }
    }

    private static async Task WriteRecoveryAsync(TextWriter error, FixApplicationResult result)
    {
        await error.WriteLineAsync($"drillpress: Fix stopped: {result.Error}");
        await WritePathsAsync(error, "changed", result.Changed);
        await WritePathsAsync(error, "failed", result.Failed is { } failed ? [failed] : []);
        await WritePathsAsync(error, "pending", result.Pending);
    }

    private static async Task WritePathsAsync(
        TextWriter error,
        string label,
        IEnumerable<string> paths
    )
    {
        foreach (var path in paths)
        {
            await error.WriteLineAsync(
                $"  {label}: {System.Text.Json.JsonEncodedText.Encode(path)}"
            );
        }
    }
}
