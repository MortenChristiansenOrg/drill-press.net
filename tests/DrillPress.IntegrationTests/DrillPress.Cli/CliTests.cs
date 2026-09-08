using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.Cli;

public sealed class CliTests : IntegrationTest
{
    [Fact]
    public async Task Check_reports_the_sample_violation_and_removes_its_snapshot()
    {
        var result = await RunCliAsync(SampleProjectPath);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
        Assert.Equal(
            """
            DP1003 Remove interfaces with exactly one concrete non-test implementation.
            Sample Solution/src/WidgetLibrary/Contracts.cs
              3:18
            DP1004 Use the empty string literal "" instead of string.Empty.
            Sample Solution/src/WidgetLibrary/Contracts.cs
              +10:29
            DP1005 Avoid passing StringComparer.Ordinal.
            Sample Solution/src/WidgetLibrary/WidgetService.cs
              7:43

            """.ReplaceLineEndings("\n"),
            result.StandardOutput);
        Assert.Empty(FileSystem.Directory.EnumerateDirectories(result.TemporaryRoot, "drillpress-*"));
    }

    [Fact]
    public async Task Check_emits_nothing_for_a_compliant_project()
    {
        var projectDirectory = CreateTemporaryDirectory("drillpress-clean-project-");
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(projectDirectory.FullName, "Clean.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <LangVersion>14.0</LangVersion>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """.ReplaceLineEndings("\n"),
            TestContext.Current.CancellationToken);
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(projectDirectory.FullName, "Clean.cs"),
            "public static class Clean { public static string Value => \"\"; }",
            TestContext.Current.CancellationToken);

        await RestoreAsync(FileSystem.Path.Combine(projectDirectory.FullName, "Clean.csproj"));

        var result = await RunCliAsync(FileSystem.Path.Combine(projectDirectory.FullName, "Clean.csproj"));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
        Assert.Equal(string.Empty, result.StandardError);
        Assert.Empty(FileSystem.Directory.EnumerateDirectories(result.TemporaryRoot, "drillpress-*"));
    }

    [Fact]
    public async Task Check_maps_tool_failure_to_exit_code_two_and_removes_its_snapshot()
    {
        var result = await RunCliAsync(RepositoryPath("missing.csproj"));

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
        Assert.Contains("was not found", result.StandardError);
        Assert.Empty(FileSystem.Directory.EnumerateDirectories(result.TemporaryRoot, "drillpress-*"));
    }

    [Fact]
    public void Coordinator_depends_only_on_the_manifest_and_filesystem_wrappers()
    {
        var projectPath = RepositoryPath("src", "DrillPress.Cli", "DrillPress.Cli.csproj");

        var projectFile = FileSystem.File.ReadAllText(projectPath);

        Assert.Equal(["../DrillPress.Manifest/DrillPress.Manifest.csproj"],
            System.Xml.Linq.XDocument.Parse(projectFile).Descendants("ProjectReference")
                .Select(reference => reference.Attribute("Include")!.Value));
        Assert.Equal(
            ["TestableIO.System.IO.Abstractions.Wrappers"],
            System.Xml.Linq.XDocument.Parse(projectFile).Descendants("PackageReference")
                .Select(reference => reference.Attribute("Include")!.Value));
    }

    private async Task<CliResult> RunCliAsync(string target)
    {
        var temporaryRoot = CreateTemporaryDirectory("drillpress-cli-test-");
        var cli = GetOutputPath("DrillPress.Cli");
        var buildHost = GetOutputPath("DrillPress.BuildHost");
        var rules = GetOutputPath("DrillPress.SampleRules", "samples");
        var environment = new Dictionary<string, string>
        {
            ["TMPDIR"] = temporaryRoot.FullName,
            ["TMP"] = temporaryRoot.FullName,
            ["TEMP"] = temporaryRoot.FullName,
        };
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = await RunProcessAsync(
            "dotnet",
            [cli, "check", "--build-host", buildHost, "--rules", rules, target],
            RepositoryRoot,
            cancellationToken,
            environment);

        return new CliResult(
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            temporaryRoot.FullName);
    }

    private sealed record CliResult(
        int ExitCode,
        string StandardOutput,
        string StandardError,
        string TemporaryRoot);
}
