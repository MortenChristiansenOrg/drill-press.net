using DrillPress.Cli;
using Xunit;

namespace DrillPress.UnitTests.Cli;

public sealed class CliApplicationTests
{
    [Theory]
    [InlineData()]
    [InlineData("check")]
    [InlineData("check", "--build-host", "host", "--rules", "rules")]
    [InlineData("check", "--build-host", "host", "--build-host", "other", "--rules", "rules", "target")]
    public async Task Run_returns_failure_and_usage_for_invalid_arguments(params string[] arguments)
    {
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            arguments,
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(
            $"Usage: drillpress check --build-host <path> --rules <path> <project.csproj>{Environment.NewLine}",
            error.ToString());
    }
}
