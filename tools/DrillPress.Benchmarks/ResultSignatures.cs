using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DrillPress.Manifest;

namespace DrillPress.Benchmarks;

public sealed class ResultSignatures(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    public async Task<ResultSignatureFiles> WriteAsync(string snapshotPath, string responsePath, string prefix, CancellationToken cancellationToken, string? normalizationRoot = null)
    {
        var snapshot = await new CompilationSnapshotFile(_fileSystem).ReadAsync(snapshotPath, cancellationToken);
        var responseBytes = await _fileSystem.File.ReadAllBytesAsync(responsePath, cancellationToken);
        var responseText = new UTF8Encoding(false, true).GetString(responseBytes);
        var plan = BundleResponseProtocol.Read(responseText, snapshot);
        var response = JsonSerializer.Deserialize(responseText, CompilationSnapshotJsonContext.Default.BundleResponse)!;
        if (normalizationRoot is not null)
        {
            var normalization = new SignatureNormalization(_fileSystem, snapshot, normalizationRoot);
            response = normalization.Response(response);
            plan = normalization.Plan(plan);
            responseBytes = BundleResponseProtocol.Serialize(response);
        }

        var planBytes = JsonSerializer.SerializeToUtf8Bytes(plan);
        var publicBytes = Encoding.UTF8.GetBytes(new CompactDiagnosticRenderer(_fileSystem).Render(plan));
        prefix = _fileSystem.Path.GetFullPath(prefix);
        _fileSystem.Directory.CreateDirectory(_fileSystem.Path.GetDirectoryName(prefix)!);
        var files = new ResultSignatureFiles(prefix + ".response.json", prefix + ".plan.json", prefix + ".public.stdout",
            Hash(responseBytes), Hash(planBytes), Hash(publicBytes), response.Contexts.Sum(context => context.Findings.Length),
            plan.Findings.Length, plan.Batches.Length, plan.Edits.Length, publicBytes.LongLength);
        await _fileSystem.File.WriteAllBytesAsync(files.ResponsePath, responseBytes, cancellationToken);
        await _fileSystem.File.WriteAllBytesAsync(files.PlanPath, planBytes, cancellationToken);
        await _fileSystem.File.WriteAllBytesAsync(files.PublicOutputPath, publicBytes, cancellationToken);
        await _fileSystem.File.WriteAllTextAsync(prefix + ".signature.json", JsonSerializer.Serialize(files,
            new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        return files;
    }

    public static void RequireEqual(ResultSignatureFiles expected, ResultSignatureFiles actual)
    {
        if (expected.ResponseHash != actual.ResponseHash || expected.PlanHash != actual.PlanHash || expected.PublicOutputHash != actual.PublicOutputHash)
        {
            throw new InvalidDataException($"Complete diagnostic/fix signatures differ: {expected.ResponsePath} and {actual.ResponsePath}.");
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
