using System.IO.Abstractions;
using DrillPress.Manifest;

namespace DrillPress.Engine;

/// <summary>Hosts a compiled rule set behind the executable rule-bundle contract.</summary>
public sealed class RuleApplication
{
    private readonly IFileSystem _fileSystem;
    private readonly CoverageProcess _coverageProcess;

    /// <summary>Creates a rule-bundle host for local compilation snapshots.</summary>
    public RuleApplication()
        : this(new FileSystem()) { }

    internal RuleApplication(IFileSystem fileSystem)
        : this(fileSystem, new CoverageProcess()) { }

    internal RuleApplication(IFileSystem fileSystem, CoverageProcess coverageProcess)
    {
        _fileSystem = fileSystem;
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
        if (
            args.Length < 2
            || args[0] != "check"
            || args.Skip(2)
                .Any(argument =>
                    argument
                        is not (
                            "--profile"
                            or "--no-optimization"
                            or "--refresh-coverage"
                            or "--explain-coverage"
                        )
                )
        )
        {
            await standardError.WriteLineAsync(
                "Usage: <rule-bundle> check <snapshot> [--profile] [--no-optimization] [--refresh-coverage] [--explain-coverage]"
            );
            return RuleExitCode.Failure;
        }

        var profile = new PipelineProfile(
            args.Skip(2).Contains("--profile"),
            standardError,
            "rules"
        );
        using var total = profile.Measure("total");
        try
        {
            CompilationSnapshot snapshot;
            using (profile.Measure("snapshot.loading"))
            {
                snapshot = await new CompilationSnapshotFile(_fileSystem).ReadAsync(
                    args[1],
                    cancellationToken
                );
            }

            var options = new AnalysisOptions
            {
                EnableOptimizations = !args.Skip(2).Contains("--no-optimization"),
                Profile = profile,
                RefreshCoverage = args.Skip(2).Contains("--refresh-coverage"),
                ExplainCoverage = args.Skip(2).Contains("--explain-coverage"),
            };
            var response = await new AnalysisEngine(_fileSystem, _coverageProcess).EvaluateAsync(
                rules,
                snapshot,
                options,
                cancellationToken
            );
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
