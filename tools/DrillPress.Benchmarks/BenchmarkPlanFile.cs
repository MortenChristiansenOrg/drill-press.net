using System.IO.Abstractions;
using System.Text.Json;

namespace DrillPress.Benchmarks;

public sealed class BenchmarkPlanFile(IFileSystem fileSystem)
{
    public BenchmarkPlanFile() : this(new FileSystem())
    {
    }

    public BenchmarkPlan Read(string path) =>
        JsonSerializer.Deserialize<BenchmarkPlan>(fileSystem.File.ReadAllText(path))
        ?? throw new InvalidOperationException("The benchmark plan is empty.");

    public Task WriteAsync(string path, BenchmarkPlan plan, CancellationToken cancellationToken = default) =>
        fileSystem.File.WriteAllTextAsync(path, JsonSerializer.Serialize(plan), cancellationToken);
}
