using System.IO.Abstractions;
using System.Text.Json;
using DrillPress.BuildHost;
using DrillPress.Engine;
using DrillPress.Manifest;
using DrillPress.SampleRules;

namespace DrillPress.Conformance;

public sealed class ConformanceApplication(IFileSystem fileSystem, MsBuildSnapshotLoader loader, AnalysisEngine engine, CompilationSnapshotFile storage)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly MsBuildSnapshotLoader _loader = loader;
    private readonly AnalysisEngine _engine = engine;
    private readonly CompilationSnapshotFile _storage = storage;

    public async Task<ConformanceExitCode> RunAsync(string[] args, CancellationToken cancellationToken = default, string? displayTarget = null)
    {
        if (args is not [var target, var reportPath])
        {
            Console.Error.WriteLine("Usage: DrillPress.Conformance <target> <report.json>");
            return ConformanceExitCode.Failure;
        }

        try
        {
            var export = await _loader.LoadCompilationsAsync(target, new SnapshotLoadOptions(), cancellationToken);
            var temporary = _fileSystem.Directory.CreateTempSubdirectory("drillpress-conformance-");
            try
            {
                var path = _fileSystem.Path.Combine(temporary.FullName, "snapshot.json");
                await _storage.WriteAsync(path, export.Snapshot, cancellationToken);
                var snapshot = await _storage.ReadAsync(path, cancellationToken);
                var engine = _engine;
                var reconstructed = engine.Reconstruct(snapshot, cancellationToken);
                var comparisons = export.Contexts.Zip(reconstructed).Select(pair => Compare(pair.First, pair.Second, cancellationToken)).ToArray();
                var liveRules = await engine.EvaluateAsync(SampleRuleSet.Create(), snapshot.RequestId, export.Contexts, cancellationToken);
                var restoredRules = await engine.EvaluateAsync(SampleRuleSet.Create(), snapshot.RequestId, reconstructed, cancellationToken);
                var ruleParity = BundleResponseProtocol.Serialize(liveRules).AsSpan().SequenceEqual(BundleResponseProtocol.Serialize(restoredRules));
                var report = new { target = displayTarget ?? target, sdk = export.Contexts.Select(context => context.Snapshot.SdkVersion).Distinct(),
                    snapshotBytes = _fileSystem.FileInfo.New(path).Length, ruleParity,
                    rules = liveRules.Contexts.SelectMany(context => context.Findings).GroupBy(finding => finding.RuleId)
                        .OrderBy(group => group.Key).Select(group => new { id = group.Key, findings = group.Count(), fixable = group.Count(finding => finding.BatchId is not null) }),
                    proposedBatches = liveRules.Batches.Length, contexts = comparisons };
                var fullReport = _fileSystem.Path.GetFullPath(reportPath);
                _fileSystem.Directory.CreateDirectory(_fileSystem.Path.GetDirectoryName(fullReport)!);
                await _fileSystem.File.WriteAllTextAsync(fullReport, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
                return ruleParity && comparisons.All(comparison => comparison.Equal) ? ConformanceExitCode.Equal : ConformanceExitCode.Mismatch;
            }
            finally
            {
                temporary.Delete(true);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var fullReport = _fileSystem.Path.GetFullPath(reportPath);
            _fileSystem.Directory.CreateDirectory(_fileSystem.Path.GetDirectoryName(fullReport)!);
            await _fileSystem.File.WriteAllTextAsync(fullReport, JsonSerializer.Serialize(new { target = displayTarget ?? target, error = exception.ToString() }), cancellationToken);
            Console.Error.WriteLine($"Conformance: {exception.Message}");
            return ConformanceExitCode.Failure;
        }
    }

    private static ContextComparison Compare(CompilationContext live, CompilationContext reconstructed, CancellationToken cancellationToken)
    {
        var expected = SemanticSignatures.Capture(live, cancellationToken);
        var actual = SemanticSignatures.Capture(reconstructed, cancellationToken);
        return new ContextComparison(live.Snapshot.Name, live.Snapshot.TargetFramework, expected.Length,
            expected.SequenceEqual(actual), expected.Except(actual).Take(10).ToArray(), actual.Except(expected).Take(10).ToArray());
    }

    private sealed record ContextComparison(string Project, string Framework, int Probes, bool Equal, string[] Missing, string[] Unexpected);
}
