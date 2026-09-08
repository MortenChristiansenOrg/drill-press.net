using DrillPress.Cli;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.IntegrationTests.TestInfrastructure;

public sealed class SnapshotPermissionFixture : IntegrationTest
{
    public bool ChildExited { get; private set; }
    public bool DirectoryRemoved { get; private set; }

    public async Task<T> CaptureAsync<T>(Func<string, string, T> inspect)
    {
        var ready = FileSystem.Path.Combine(CreateTemporaryDirectory("drillpress-permissions-").FullName, "ready");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new CliApplication().RunAsync(
            ["check", "--build-host", GetOutputPath("DrillPress.TestProcess", "tests"), "--rules", "unused", ready],
            TextWriter.Null, cancellation.Token);
        var process = await WaitForTestProcessAsync(ready, run, cancellation);
        var snapshot = await FileSystem.File.ReadAllTextAsync(ready + ".snapshot", TestContext.Current.CancellationToken);
        var directory = FileSystem.Path.GetDirectoryName(snapshot)!;
        try
        {
            await new CompilationSnapshotFile().WriteAsync(snapshot, CompilationSnapshot.Create(), TestContext.Current.CancellationToken);
            return inspect(directory, snapshot);
        }
        finally
        {
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            ChildExited = process.HasExited;
            DirectoryRemoved = !FileSystem.Directory.Exists(directory);
        }
    }
}
