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
        var process = await WaitForTestProcessAsync(readyPath, run, cancellation);
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
        var process = await WaitForTestProcessAsync(readyPath, run, cancellation);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.True(process.HasExited);
    }

    [Fact]
    public async Task Readiness_timeout_cancels_and_awaits_the_launch_before_returning()
    {
        var directory = CreateTemporaryDirectory("drillpress-readiness-timeout-");
        var readyPath = Path.Combine(directory.FullName, "ready");
        var missingReadyPath = Path.Combine(directory.FullName, "never-ready");
        var testProcess = GetOutputPath("DrillPress.TestProcess", "tests");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = RunProcessAsync(
            "dotnet", [testProcess, "export", readyPath, "unused.snapshot"], RepositoryRoot, cancellation.Token);
        var process = await WaitForTestProcessAsync(readyPath, run, cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            WaitForTestProcessAsync(missingReadyPath, run, cancellation, TimeSpan.FromMilliseconds(50)));

        Assert.True(run.IsCanceled);
        Assert.True(process.HasExited);
    }

    [Fact]
    public async Task Readiness_wait_reports_a_process_that_exits_before_becoming_ready()
    {
        var directory = CreateTemporaryDirectory("drillpress-readiness-exit-");
        var readyPath = Path.Combine(directory.FullName, "never-ready");
        var testProcess = GetOutputPath("DrillPress.TestProcess", "tests");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = RunProcessAsync("dotnet", [testProcess], RepositoryRoot, cancellation.Token);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WaitForTestProcessAsync(readyPath, run, cancellation));

        Assert.True(run.IsCompletedSuccessfully);
        Assert.Equal("The test process exited before reporting readiness.", exception.Message);
    }
}
