using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;

namespace DrillPress.Benchmarks;

public sealed class RepositoryIdentity(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    public async Task WriteAsync(RepositoryMeasurements measurements, string root, string output, CancellationToken cancellationToken)
    {
        var status = await measurements.RunAsync("tool-status", "git", ["status", "--porcelain=v1"], root, false, [], cancellationToken);
        var difference = await measurements.RunAsync("tool-diff", "git", ["diff", "--binary", "HEAD"], root, false, [], cancellationToken);
        var inventory = await measurements.RunAsync("tool-files", "git", ["ls-files", "-z", "--cached", "--others", "--exclude-standard"], root, false, [], cancellationToken);
        var paths = (await _fileSystem.File.ReadAllTextAsync(inventory.StandardOutputPath, cancellationToken)).Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct().Order();
        var files = new List<object>();
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var absolute = _fileSystem.Path.Combine(root, path);
            files.Add(new { path, sha256 = _fileSystem.File.Exists(absolute)
                ? Convert.ToHexString(SHA256.HashData(await _fileSystem.File.ReadAllBytesAsync(absolute, cancellationToken))) : null });
        }

        await _fileSystem.File.WriteAllTextAsync(_fileSystem.Path.Combine(output, "tool-source.json"), JsonSerializer.Serialize(new
        {
            status = await _fileSystem.File.ReadAllTextAsync(status.StandardOutputPath, cancellationToken),
            diff = difference.StandardOutputPath, files,
        }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }
}
