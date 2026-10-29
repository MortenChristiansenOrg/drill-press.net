using System.IO.Pipes;
using System.Text.Json;
using DrillPress.Engine;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.Engine;

public sealed class RuleWorkerTests : IntegrationTest
{
    [Fact]
    public async Task Disconnect_retires_a_worker_even_while_a_rule_does_not_observe_cancellation()
    {
        var directory = CreateTemporaryDirectory("drillpress-worker-cancel-");
        var source = FileSystem.Path.Combine(directory.FullName, "A.cs");
        var snapshotPath = FileSystem.Path.Combine(directory.FullName, "snapshot.json");
        await FileSystem.File.WriteAllTextAsync(
            source,
            "public static class A {}",
            TestContext.Current.CancellationToken
        );
        var captured = await RunProcessAsync(
            "dotnet",
            [GetOutputPath("DrillPress.BuildHost"), "export", source, snapshotPath],
            directory.FullName,
            TestContext.Current.CancellationToken
        );
        using var blocking = new BlockingFix();
        var name = "drillpress-test-" + Guid.NewGuid().ToString("N");
        var server = new RuleApplication().RunAsync(
            blocking.Rules(),
            [CommandWorker.Command, name],
            cancellationToken: TestContext.Current.CancellationToken
        );
        using var pipe = new NamedPipeClientStream(
            ".",
            name,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
        );
        await pipe.ConnectAsync(5000, TestContext.Current.CancellationToken);
        await CommandWorker.WriteAsync(
            pipe,
            JsonSerializer.SerializeToUtf8Bytes(
                new[] { "check", snapshotPath },
                WorkerJsonContext.Default.StringArray
            ),
            TestContext.Current.CancellationToken
        );
        await blocking.Started.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken
        );

        pipe.Dispose();
        var outcome = await server.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(RuleExitCode.Clean, outcome);
        Assert.Equal(
            (0, "", ""),
            (captured.ExitCode, captured.StandardOutput, captured.StandardError)
        );
    }
}
