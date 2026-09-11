using System.Text;
using DrillPress.Cli;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.Cli;

public sealed class ChildProcessRunnerTests : IntegrationTest
{
    [Fact]
    public async Task Invalid_UTF8_is_rejected_after_both_pipes_are_fully_drained()
    {
        var process = GetOutputPath("DrillPress.TestProcess", "tests");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        cancellation.CancelAfter(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<DecoderFallbackException>(() =>
            new ChildProcessRunner().CaptureAsync(process, ["bytes"], cancellation.Token)
        );
    }

    [Theory]
    [InlineData("stdout", "standard output")]
    [InlineData("stderr", "standard error")]
    public async Task Oversized_output_terminates_an_otherwise_unending_child(
        string pipe,
        string description
    )
    {
        var process = GetOutputPath("DrillPress.TestProcess", "tests");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        cancellation.CancelAfter(TimeSpan.FromSeconds(30));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ChildProcessRunner(1024, 1024).CaptureAsync(
                process,
                ["limited-output", pipe],
                cancellation.Token
            )
        );

        Assert.Equal($"Child {description} exceeded 1024 bytes.", exception.Message);
    }
}
