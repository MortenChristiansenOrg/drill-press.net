using DrillPress.BuildHost;
using Xunit;

namespace DrillPress.UnitTests.BuildHost;

public sealed class BuildHostApplicationTests
{
    [Fact]
    public async Task Run_returns_failure_and_usage_for_invalid_arguments()
    {
        var error = new StringWriter();

        var exitCode = await BuildHostApplication.RunAsync(
            ["export"],
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(BuildHostExitCode.Failure, exitCode);
        Assert.Equal(
            $"Usage: DrillPress.BuildHost export <project.csproj> <snapshot>{Environment.NewLine}",
            error.ToString());
    }
}
