using DrillPress.Cli;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.Cli;

public sealed class ProcessCancellationTests : IntegrationTest
{
    [Fact]
    public async Task Cli_cancellation_stops_the_child_before_removing_the_snapshot_directory()
    {
        var directory = CreateTemporaryDirectory("drillpress-cancellation-");
        var readyPath = FileSystem.Path.Combine(directory.FullName, "ready");
        var testProcess = GetOutputPath("DrillPress.TestProcess", "tests");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var run = new CliApplication().RunAsync(
            ["check", "--build-host", testProcess, "--rules", "unused.dll", readyPath],
            TextWriter.Null,
            cancellation.Token);
        var process = await WaitForTestProcessAsync(readyPath, run, cancellation);
        var snapshotPath = await FileSystem.File.ReadAllTextAsync(readyPath + ".snapshot", TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.True(process.HasExited);
        Assert.False(FileSystem.Directory.Exists(FileSystem.Path.GetDirectoryName(snapshotPath)));
    }
}
