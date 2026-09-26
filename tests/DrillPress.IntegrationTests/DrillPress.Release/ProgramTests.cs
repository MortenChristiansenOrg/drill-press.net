using System.Reflection;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.Release;

public sealed class ProgramTests : IntegrationTest
{
    [Fact]
    public async Task Invalid_tag_returns_a_clean_failure_message()
    {
        var tool = ReleaseToolPath();

        var result = await RunProcessAsync(
            "dotnet",
            [tool, "version", "v1.2"],
            RepositoryRoot,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            (
                1,
                "",
                $"Expected vMAJOR.MINOR.PATCH[-PRERELEASE], without leading zeroes or build metadata.{Environment.NewLine}"
            ),
            (result.ExitCode, result.StandardOutput, result.StandardError)
        );
    }

    [Fact]
    public async Task Missing_packages_return_a_clean_failure_message()
    {
        var tool = ReleaseToolPath();
        var feed = CreateTemporaryDirectory("drillpress-empty-feed-").FullName;

        var result = await RunProcessAsync(
            "dotnet",
            [tool, "validate", "v1.0.0", feed],
            RepositoryRoot,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            (
                1,
                "",
                $"Release must contain exactly the five Drill Press packages.{Environment.NewLine}"
            ),
            (result.ExitCode, result.StandardOutput, result.StandardError)
        );
    }

    private static string ReleaseToolPath() =>
        RepositoryPath(
            "tools",
            "DrillPress.Release",
            "bin",
            typeof(ProgramTests)
                .Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!
                .Configuration,
            "net10.0",
            "DrillPress.Release.dll"
        );
}
