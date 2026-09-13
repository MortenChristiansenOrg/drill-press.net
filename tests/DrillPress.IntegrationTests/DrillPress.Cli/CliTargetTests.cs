using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.Cli;

public sealed class CliTargetTests : IntegrationTest
{
    [Fact]
    public async Task Ambiguous_directory_fails_before_loading_and_cleans_its_snapshot()
    {
        var root = CreateTemporaryDirectory("drillpress-ambiguous-cli-").FullName;
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(root, "First.slnx"),
            "<Solution />",
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(root, "Second.slnx"),
            "<Solution />",
            TestContext.Current.CancellationToken
        );

        var result = await RunCliAsync(root);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.Equal(
            $"DrillPress.BuildHost: Directory '{root}' contains 2 eligible targets; specify a .sln, .slnx, or .csproj file.{Environment.NewLine}drillpress: BuildHost exited 2.{Environment.NewLine}",
            result.StandardError
        );
        Assert.Empty(
            FileSystem.Directory.EnumerateDirectories(result.TemporaryRoot, "drillpress-*")
        );
    }

    [Fact]
    public async Task Zero_match_glob_fails_and_cleans_its_snapshot()
    {
        var root = CreateTemporaryDirectory("drillpress-zero-cli-").FullName;
        var target = FileSystem.Path.Combine(root, "**", "*.cs");

        var result = await RunCliAsync(target);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.Equal(
            $"DrillPress.BuildHost: C# glob '{target}' matched no files.{Environment.NewLine}drillpress: BuildHost exited 2.{Environment.NewLine}",
            result.StandardError
        );
        Assert.Empty(
            FileSystem.Directory.EnumerateDirectories(result.TemporaryRoot, "drillpress-*")
        );
    }

    [Fact]
    public async Task Missing_assets_fail_without_restoring_or_changing_source()
    {
        var root = CreateTemporaryDirectory("drillpress-restore-cli-").FullName;
        var target = await CreateProjectAsync(root);
        var source = FileSystem.Path.Combine(root, "Source.cs");
        const string text = "class Source { string Value => string.Empty; }";
        await FileSystem.File.WriteAllTextAsync(
            source,
            text,
            TestContext.Current.CancellationToken
        );

        var result = await RunCliAsync(target);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.Equal(
            $"DrillPress.BuildHost: Restore assets are missing for '{target}'. Run dotnet restore for the target first.{Environment.NewLine}drillpress: BuildHost exited 2.{Environment.NewLine}",
            result.StandardError
        );
        Assert.Equal(
            text,
            await FileSystem.File.ReadAllTextAsync(source, TestContext.Current.CancellationToken)
        );
        Assert.False(
            FileSystem.File.Exists(FileSystem.Path.Combine(root, "obj", "project.assets.json"))
        );
        Assert.Empty(
            FileSystem.Directory.EnumerateDirectories(result.TemporaryRoot, "drillpress-*")
        );
    }

    [Fact]
    public async Task Target_global_json_controls_sdk_selection_from_the_repository_directory()
    {
        var root = CreateTemporaryDirectory("drillpress-sdk-cli-").FullName;
        var target = await CreateProjectAsync(root);
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(root, "global.json"),
            """{"sdk":{"version":"99.0.100","rollForward":"disable"}}""",
            TestContext.Current.CancellationToken
        );

        var result = await RunCliAsync(target);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.Equal(
            $"DrillPress.BuildHost: No compatible .NET SDK was found for '{root}'. Install the SDK required by its global.json.{Environment.NewLine}drillpress: BuildHost exited 2.{Environment.NewLine}",
            result.StandardError
        );
        Assert.Empty(
            FileSystem.Directory.EnumerateDirectories(result.TemporaryRoot, "drillpress-*")
        );
    }

    private async Task<string> CreateProjectAsync(string root)
    {
        var target = FileSystem.Path.Combine(root, "Target.csproj");
        await FileSystem.File.WriteAllTextAsync(
            target,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
            TestContext.Current.CancellationToken
        );
        return target;
    }
}
