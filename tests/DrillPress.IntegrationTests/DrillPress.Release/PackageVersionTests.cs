using System.Reflection;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.IntegrationTests.Release;

public sealed class PackageVersionTests : IntegrationTest
{
    [Fact]
    public async Task Packing_another_suffix_without_rebuilding_fails()
    {
        var assembly = typeof(CompilationSnapshot).Assembly;
        var version =
            assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                .InformationalVersion + "-stale";
        var configuration = assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()!
            .Configuration;
        var output = CreateTemporaryDirectory("drillpress-stale-package-").FullName;

        var result = await RunProcessAsync(
            "dotnet",
            [
                "pack",
                RepositoryPath("src", "DrillPress.Manifest", "DrillPress.Manifest.csproj"),
                "--no-build",
                "--no-restore",
                "-c",
                configuration,
                "-p:Version=" + version,
                "-o",
                output,
            ],
            RepositoryRoot,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            $"Rebuild with Version={version} before packing; the built package version differs.",
            result.StandardOutput
        );
        Assert.Empty(FileSystem.Directory.GetFiles(output, "*.nupkg"));
    }
}
