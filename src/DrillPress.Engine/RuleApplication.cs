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
    /// clean, findings, or failure exit code understood by the coordinator. An accepted
    /// baseline passed by the coordinator is ignored, because these rules cannot read it.
    /// </summary>
    public Task<RuleExitCode> RunAsync(
        RuleCatalog rules,
        string[] args,
        TextWriter? standardOutput = null,
        TextWriter? standardError = null,
        CancellationToken cancellationToken = default
    ) => RunAsync(_ => rules, false, args, standardOutput, standardError, cancellationToken);

    /// <summary>
    /// Executes the rule-bundle command with rules created after the snapshot is loaded. When
    /// <c>drillpress check --baseline</c> supplies an accepted state, <paramref name="createRules"/>
    /// receives it as a <see cref="SourceBaseline"/>; otherwise it receives null.
    /// </summary>
    public Task<RuleExitCode> RunAsync(
        Func<SourceBaseline?, RuleCatalog> createRules,
        string[] args,
        TextWriter? standardOutput = null,
        TextWriter? standardError = null,
        CancellationToken cancellationToken = default
    ) => RunAsync(createRules, true, args, standardOutput, standardError, cancellationToken);

    private async Task<RuleExitCode> RunAsync(
        Func<SourceBaseline?, RuleCatalog> createRules,
        bool readsBaseline,
        string[] args,
        TextWriter? standardOutput,
        TextWriter? standardError,
        CancellationToken cancellationToken
    )
    {
        standardOutput ??= Console.Out;
        standardError ??= Console.Error;
        var generated = args.Length > 0 && args[0] == "check-generated";
        var flagsStart = generated ? 4 : 2;
        var flags = args.Skip(flagsStart).ToList();
        var baselineIndex = flags.IndexOf("--baseline");
        string? baseline = null;
        if (baselineIndex >= 0 && baselineIndex + 1 < flags.Count)
        {
            baseline = flags[baselineIndex + 1];
            flags.RemoveRange(baselineIndex, 2);
        }
        if (
            args.Length < flagsStart
            || (!generated && args[0] != "check")
            || flags.Any(argument =>
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
            || (flags.Contains("--refresh-coverage") && flags.Contains("--skip-coverage"))
        )
        {
            await standardError.WriteLineAsync(
                "Usage: <rule-bundle> check <snapshot> [--baseline <accepted-sources>] [--profile] [--parallel-queries] [--no-optimization] [--refresh-coverage] [--skip-coverage] [--explain-coverage]"
            );
            return RuleExitCode.Failure;
        }

        var rules = new RuleCatalogSource(
            _fileSystem,
            createRules,
            readsBaseline ? baseline : null
        );
        var profile = new PipelineProfile(flags.Contains("--profile"), standardError, "rules");
        using var total = profile.Measure("total");
        try
        {
            var options = new AnalysisOptions
            {
                EnableOptimizations = !flags.Contains("--no-optimization"),
                Profile = profile,
                RefreshCoverage = flags.Contains("--refresh-coverage"),
                SkipCoverage = flags.Contains("--skip-coverage"),
                ExplainCoverage = flags.Contains("--explain-coverage"),
                QueryPreparationConcurrency = flags.Contains("--parallel-queries")
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
                    await rules.CreateAsync(snapshot, options, cancellationToken),
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
