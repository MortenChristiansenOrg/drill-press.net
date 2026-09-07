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
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<DecoderFallbackException>(() =>
            new ChildProcessRunner().CaptureAsync(process, ["bytes"], cancellation.Token));
    }
}
