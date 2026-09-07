using DrillPress.BundleVerification;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.BundleVerification;

public sealed class BundleProcessTests : IntegrationTest
{
    [Fact]
    public async Task Captures_binary_output_and_drains_both_pipes()
    {
        var process = GetOutputPath("DrillPress.TestProcess", "tests");

        var result = await ProcessRunner.RunAsync("dotnet", [process, "bytes"], RepositoryRoot, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(Enumerable.Repeat((byte)255, 200_000), result.StandardOutput);
        Assert.Equal(Enumerable.Repeat((byte)0, 200_000), result.StandardError);
    }

    [Fact]
    public async Task Cancellation_stops_the_child_before_returning()
    {
        var directory = CreateTemporaryDirectory("drillpress-verification-cancel-");
        var ready = FileSystem.Path.Combine(directory.FullName, "ready");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = ProcessRunner.RunAsync("dotnet",
            [GetOutputPath("DrillPress.TestProcess", "tests"), "export", ready, "snapshot"], RepositoryRoot, cancellation.Token);
        var process = await WaitForTestProcessAsync(ready, run, cancellation);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.True(process.HasExited);
    }
}
