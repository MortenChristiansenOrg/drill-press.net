using DrillPress.Cli;
using Xunit;

namespace DrillPress.IntegrationTests;

public sealed class ProcessCancellationTests : IntegrationTest
{
    [Fact]
    public async Task Cli_cancellation_stops_the_child_before_removing_the_snapshot_directory()
    {
        var directory = CreateTemporaryDirectory("drillpress-cancellation-");
        var readyPath = Path.Combine(directory.FullName, "ready");
        var testProcess = GetOutputPath("DrillPress.TestProcess", "tests");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var run = CliApplication.RunAsync(
            ["check", "--build-host", testProcess, "--rules", "unused.dll", readyPath],
            TextWriter.Null,
            cancellation.Token);
        var process = await WaitForTestProcessAsync(readyPath);
        var snapshotPath = await File.ReadAllTextAsync(readyPath + ".snapshot", TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.True(process.HasExited);
        Assert.False(Directory.Exists(Path.GetDirectoryName(snapshotPath)));
    }

    [Fact]
    public async Task Test_runner_cancellation_stops_the_child()
    {
        var directory = CreateTemporaryDirectory("drillpress-runner-cancellation-");
        var readyPath = Path.Combine(directory.FullName, "ready");
        var testProcess = GetOutputPath("DrillPress.TestProcess", "tests");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var run = RunProcessAsync(
            "dotnet", [testProcess, "export", readyPath, "unused.snapshot"], RepositoryRoot, cancellation.Token);
        var process = await WaitForTestProcessAsync(readyPath);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.True(process.HasExited);
    }
}
