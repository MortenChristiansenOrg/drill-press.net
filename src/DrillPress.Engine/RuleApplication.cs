using System.IO.Abstractions;
using System.Reflection;
using DrillPress.Manifest;

namespace DrillPress.Engine;

/// <summary>Hosts a compiled rule set behind the executable rule-bundle contract.</summary>
public sealed class RuleApplication
{
    private readonly IFileSystem _fileSystem;
    private readonly CoverageProcess _coverageProcess;
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
    {
        _fileSystem = fileSystem;
        _sourceFiles = sourceFiles;
        _generators = generators;
        _coverageProcess = coverageProcess;
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
                    ?? SourceGeneratorCatalog.Empty;
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
                response = await new AnalysisEngine(_fileSystem, _coverageProcess).EvaluateAsync(
                    rules,
                    snapshot,
                    options,
                    cancellationToken
                );
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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await standardError.WriteLineAsync($"drillpress-rules: {exception.Message}");
            return RuleExitCode.Failure;
        }
    }
}
