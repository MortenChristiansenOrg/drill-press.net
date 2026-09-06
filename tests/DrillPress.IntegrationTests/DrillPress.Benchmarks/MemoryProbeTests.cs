using System.Text.Json;
using DrillPress.Benchmarks;
using DrillPress.BundleVerification;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.Benchmarks;

public sealed class MemoryProbeTests : IntegrationTest
{
    [Fact]
    public async Task Fresh_workers_preserve_binary_contracts_and_report_child_memory()
    {
        var directory = CreateTemporaryDirectory("drillpress-memory-probe-");
        var scenario = new BundleCase("bytes", ["bytes"], BundleOutcome.Failure,
            Enumerable.Repeat((byte)255, 200_000).ToArray(), new byte[200_000]);
        var plan = new BenchmarkPlan(RepositoryRoot, GetOutputPath("DrillPress.TestProcess", "tests"),
            "", [scenario]);
        var planPath = Path.Combine(directory.FullName, "plan.json");
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan), TestContext.Current.CancellationToken);

        var output = await ProcessRunner.RunAsync("dotnet",
            [GetOutputPath("DrillPress.Benchmarks", "tools"), "--memory-worker", planPath, "Managed", "bytes"],
            RepositoryRoot, TestContext.Current.CancellationToken);

        Assert.Equal(0, output.ExitCode);
        Assert.Empty(output.StandardError);
        var sample = JsonSerializer.Deserialize<MemorySample>(output.StandardOutput);
        Assert.NotNull(sample);
        Assert.Equal(BundleMode.Managed, sample.Mode);
        Assert.Equal("bytes", sample.Case);
        Assert.True(sample.PeakMemoryBytes > 0);
        Assert.Equal(2, sample.Output.ExitCode);
        Assert.Equal(scenario.StandardOutput, sample.Output.StandardOutput);
        Assert.Equal(scenario.StandardError, sample.Output.StandardError);
    }
}
