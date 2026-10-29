using System.IO.Abstractions;
using System.Reflection;
using DrillPress.Manifest;

namespace DrillPress.Engine;

/// <summary>Hosts a compiled rule set behind the executable rule-bundle contract.</summary>
public sealed class RuleApplication
{
    private readonly IFileSystem _fileSystem;
    private readonly CoverageProcess _coverageProcess;
    private readonly AnalysisEngine _engine;
    private readonly SourceFilePolicy _sourceFiles;
    private readonly SourceGeneratorCatalog? _generators;

    /// <summary>Creates a rule-bundle host for local compilation snapshots.</summary>
    public RuleApplication()
        : this(new FileSystem()) { }

    internal RuleApplication(IFileSystem fileSystem)
        : this(fileSystem, new CoverageProcess()) { }

    internal RuleApplication(IFileSystem fileSystem, CoverageProcess coverageProcess)
        : this(
            fileSystem,
            coverageProcess,
            new SourceFilePolicy(fileSystem, new FileIdentityProbe())
        ) { }

    internal RuleApplication(
        IFileSystem fileSystem,
        CoverageProcess coverageProcess,
        SourceFilePolicy sourceFiles,
        SourceGeneratorCatalog? generators = null
    )
        : this(fileSystem, coverageProcess, false, sourceFiles, generators) { }

    private RuleApplication(
        IFileSystem fileSystem,
        CoverageProcess coverageProcess,
        bool reuseCompilations,
        SourceFilePolicy sourceFiles,
        SourceGeneratorCatalog? generators
    )
    {
        _fileSystem = fileSystem;
        _sourceFiles = sourceFiles;
        _generators = generators;
        _coverageProcess = coverageProcess;
        _engine = new AnalysisEngine(fileSystem, coverageProcess, reuseCompilations);
    }

    /// <summary>
    /// Executes the rule-bundle command, writes the internal response, and returns the
    /// clean, findings, or failure exit code understood by the coordinator.
    /// </summary>
    public async Task<RuleExitCode> RunAsync(
        RuleCatalog rules,
        string[] args,
        TextWriter? standardOutput = null,
        TextWriter? standardError = null,
        CancellationToken cancellationToken = default
    )
    {
        standardOutput ??= Console.Out;
        standardError ??= Console.Error;
        if (args is [CommandWorker.Command, var name])
        {
            var worker = new RuleApplication(
                _fileSystem,
                _coverageProcess,
                reuseCompilations: true,
                _sourceFiles,
                _generators
            );
            await new CommandWorker().RunAsync(
                name,
                async (arguments, token) =>
                {
                    using var output = new BoundedStringWriter(64 * 1024 * 1024, "standard output");
                    using var error = new BoundedStringWriter(8 * 1024 * 1024, "standard error");
                    var code = await worker.RunAsync(rules, arguments, output, error, token);
                    return new WorkerResponse((int)code, output.ToString(), error.ToString());
                },
                cancellationToken
            );
            return RuleExitCode.Clean;
        }
        var generated = args.Length > 0 && args[0] == "check-generated";
        var flagsStart = generated ? 4 : 2;
        if (
            args.Length < flagsStart
            || (!generated && args[0] != "check")
            || args.Skip(flagsStart)
                .Any(argument =>
                    argument
                        is not (
                            "--profile"
                            or "--no-optimization"
                            or "--refresh-coverage"
                            or "--skip-coverage"
                            or "--explain-coverage"
                            or "--parallel-queries"
                        )
                )
            || (args.Contains("--refresh-coverage") && args.Contains("--skip-coverage"))
        )
        {
            await standardError.WriteLineAsync(
                "Usage: <rule-bundle> check <snapshot> [--profile] [--parallel-queries] [--no-optimization] [--refresh-coverage] [--skip-coverage] [--explain-coverage]"
            );
            return RuleExitCode.Failure;
        }

        var profile = new PipelineProfile(
            args.Skip(flagsStart).Contains("--profile"),
            standardError,
            "rules"
        );
        using var total = profile.Measure("total");
        try
        {
            var options = new AnalysisOptions
            {
                EnableOptimizations = !args.Skip(flagsStart).Contains("--no-optimization"),
                Profile = profile,
                RefreshCoverage = args.Skip(flagsStart).Contains("--refresh-coverage"),
                SkipCoverage = args.Skip(flagsStart).Contains("--skip-coverage"),
                ExplainCoverage = args.Skip(flagsStart).Contains("--explain-coverage"),
                QueryPreparationConcurrency = args.Skip(flagsStart).Contains("--parallel-queries")
                    ? Math.Min(generated ? 8 : 4, Environment.ProcessorCount)
                    : 1,
            };
            CompilationSnapshot snapshot;
            BundleResponse response;
            if (generated)
            {
                var catalog =
                    _generators
                    ?? Assembly
                        .GetEntryAssembly()
                        ?.GetCustomAttribute<SourceGeneratorCatalogAttribute>()
                        ?.Create()
                    ?? throw new InvalidOperationException(
                        "The rule bundle has no compiled source generator catalogue."
                    );
                var result = await new SourceGenerationSession(
                    _fileSystem,
                    _coverageProcess,
                    _sourceFiles
                ).RunAsync(rules, catalog, args[1], args[2], args[3], options, cancellationToken);
                snapshot = result.Snapshot;
                response = result.Response;
            }
            else
            {
                using (profile.Measure("snapshot.loading"))
                    snapshot = await new CompilationSnapshotFile(_fileSystem).ReadAsync(
                        args[1],
                        cancellationToken
                    );
                response = await _engine.EvaluateAsync(rules, snapshot, options, cancellationToken);
            }
            ValidatedResult plan;
            using (profile.Measure("aggregation"))
            {
                plan = new BundleResponseValidator().Validate(snapshot, response);
            }

            profile.Count("contexts", snapshot.Projects.Length);
            profile.Count(
                "context.findings",
                response.Contexts.Sum(context => context.Findings.Length)
            );
            profile.Count("actionable.locations", plan.Findings.Length);
            profile.Count("common.safe.batches", plan.Batches.Length);
            profile.Count("common.safe.edits", plan.Edits.Length);
            using (profile.Measure("response.serialization"))
            {
                await standardOutput.WriteAsync(
                    System.Text.Encoding.UTF8.GetString(BundleResponseProtocol.Serialize(response))
                );
            }
            return plan.Findings.All(finding => finding.Disposition == FindingDisposition.Review)
                ? RuleExitCode.Clean
                : RuleExitCode.Findings;
        }
        catch (OperationCanceledException)
        {
            _engine.Reset();
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _engine.Reset();
            await standardError.WriteLineAsync($"drillpress-rules: {exception.Message}");
            return RuleExitCode.Failure;
        }
    }
}
