using System.IO.Abstractions;
using DrillPress.Conformance;
using DrillPress.Manifest;

namespace DrillPress.Benchmarks;

public sealed class RepositoryFixBenchmark(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    public async Task<ResultSignatureFiles> RunAsync(RepositoryMeasurements measurements, RepositoryWorkload workload,
        RepositoryVariant variant, string originalSnapshot, ResultSignatureFiles initial, string root, string output,
        string prefix, CancellationToken cancellationToken)
    {
        using var copy = new DisposableRepository(_fileSystem, workload.Root, cancellationToken);
        var target = await new PinnedXunit(_fileSystem).PrepareAsync(copy.Root,
            _fileSystem.Path.Combine(output, prefix, "copy-preparation.json"), cancellationToken);
        var snapshot = _fileSystem.Path.Combine(output, prefix, "before-fix.snapshot.json");
        await RepositoryBenchmark.ExportAsync(measurements, prefix + "/copy-export", target, snapshot, root, cancellationToken);
        var storage = new CompilationSnapshotFile(_fileSystem);
        var original = await storage.ReadAsync(originalSnapshot, cancellationToken);
        var copied = await storage.ReadAsync(snapshot, cancellationToken);
        var expectedInput = new SignatureNormalization(_fileSystem, original, workload.Root).Snapshot(original);
        var actualInput = new SignatureNormalization(_fileSystem, copied, copy.Root).Snapshot(copied);
        var expectedHash = SignatureNormalization.Hash(expectedInput);
        var actualHash = SignatureNormalization.Hash(actualInput);
        await _fileSystem.File.WriteAllTextAsync(_fileSystem.Path.Combine(output, prefix, "copy-input-signature.json"),
            System.Text.Json.JsonSerializer.Serialize(new { expectedHash, actualHash }), cancellationToken);
        if (expectedHash != actualHash)
        {
            throw new InvalidDataException($"Disposable copy changed compiler inputs or fix eligibility: {snapshot}");
        }

        var signatures = new ResultSignatures(_fileSystem);
        var response = await RepositoryBenchmark.EvaluateAsync(measurements, prefix + "/copy-initial", variant, snapshot, root, cancellationToken);
        var initialCopy = await signatures.WriteAsync(snapshot, response.StandardOutputPath, _fileSystem.Path.Combine(output, prefix, "copy-initial"), cancellationToken, copy.Root);
        ResultSignatures.RequireEqual(initial, initialCopy);
        var fix = await RepositoryBenchmark.CliAsync(measurements, prefix + "/fix", "fix", variant, target, root, cancellationToken);
        RepositoryBenchmark.RequireProfileTotals(measurements.Operations.Last().Profile, initial.SafeEdits > 0 ? 2 : 1);
        var afterSnapshot = _fileSystem.Path.Combine(output, prefix, "after-fix.snapshot.json");
        await RepositoryBenchmark.ExportAsync(measurements, prefix + "/after-export", target, afterSnapshot, root, cancellationToken);
        var after = await RepositoryBenchmark.EvaluateAsync(measurements, prefix + "/after-rules", variant, afterSnapshot, root, cancellationToken);
        await RepositoryBenchmark.RequirePublicAsync(_fileSystem, afterSnapshot, after.StandardOutputPath, fix.StandardOutputPath, cancellationToken);
        return await signatures.WriteAsync(afterSnapshot, after.StandardOutputPath, _fileSystem.Path.Combine(output, prefix, "rechecked"), cancellationToken, copy.Root);
    }
}
