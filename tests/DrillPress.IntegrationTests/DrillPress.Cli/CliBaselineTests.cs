using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.Cli;

public sealed class CliBaselineTests : IntegrationTest
{
    [Fact]
    public async Task Check_reports_change_aware_findings_against_a_git_revision()
    {
        var repository = CreateTemporaryDirectory("drillpress-baseline-").FullName;
        var project = FileSystem.Path.Combine(repository, "CodecExamples");
        FileSystem.Directory.CreateDirectory(project);
        var projectFile = FileSystem.Path.Combine(project, "CodecExamples.csproj");
        await WriteAsync(
            projectFile,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <LangVersion>14.0</LangVersion>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """
        );
        await WriteAsync(
            FileSystem.Path.Combine(project, "Existing.Legacy.cs"),
            "namespace CodecExamples;\npublic sealed class Existing { }\n"
        );
        await GitAsync(repository, "init", "--quiet");
        await GitAsync(repository, "add", "--all");
        await GitAsync(
            repository,
            "-c",
            "user.name=Drill Press",
            "-c",
            "user.email=tests@drillpress.invalid",
            "-c",
            "commit.gpgsign=false",
            "commit",
            "--quiet",
            "--message",
            "Accepted state"
        );
        await WriteAsync(
            FileSystem.Path.Combine(project, "Existing.Legacy.cs"),
            "namespace CodecExamples;\npublic sealed class Existing { public int Value; }\n"
        );
        var added = FileSystem.Path.Combine(project, "Added.Legacy.cs");
        await WriteAsync(added, "namespace CodecExamples;\npublic sealed class Added { }\n");
        await RestoreAsync(projectFile);

        var result = await RunCliAsync(projectFile, "--baseline", "HEAD");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
        Assert.Equal(
            $"""
            SDK2013 Add new codec implementations to the current format layer.
            {FileSystem.Path.GetRelativePath(RepositoryRoot, added).Replace('\\', '/')}
              1

            """.ReplaceLineEndings("\n"),
            result.StandardOutput
        );
        Assert.Empty(
            FileSystem.Directory.EnumerateDirectories(result.TemporaryRoot, "drillpress-*")
        );
    }

    private static Task WriteAsync(string path, string text) =>
        FileSystem.File.WriteAllTextAsync(
            path,
            text.ReplaceLineEndings("\n"),
            TestContext.Current.CancellationToken
        );

    private static async Task GitAsync(string repository, params string[] arguments)
    {
        var result = await RunProcessAsync(
            "git",
            ["-C", repository, .. arguments],
            repository,
            TestContext.Current.CancellationToken
        );
        Assert.True(result.ExitCode == 0, result.StandardError);
    }
}
