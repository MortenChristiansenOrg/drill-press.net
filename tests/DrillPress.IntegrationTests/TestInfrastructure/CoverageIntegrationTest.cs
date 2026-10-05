using System.Xml.Linq;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.IntegrationTests.TestInfrastructure;

public abstract class CoverageIntegrationTest : IntegrationTest
{
    protected async Task<CompilationSnapshot> CreateCoverageSnapshotAsync(
        string source,
        string tests
    )
    {
        var directory = CreateTemporaryDirectory("drillpress-coverage-scenario-").FullName;
        var target = FileSystem.Directory.CreateDirectory(
            FileSystem.Path.Combine(directory, "Target")
        );
        var testDirectory = FileSystem.Directory.CreateDirectory(
            FileSystem.Path.Combine(directory, "Tests")
        );
        await WriteAsync(directory, "Workspace.slnx", "<Solution />");
        await WriteAsync(
            directory,
            "global.json",
            FileSystem.File.ReadAllText(RepositoryPath("global.json"))
        );
        await WriteAsync(
            directory,
            "Directory.Packages.props",
            new XElement(
                "Project",
                new XElement(
                    "Import",
                    new XAttribute("Project", RepositoryPath("Directory.Packages.props"))
                )
            ).ToString()
        );
        await WriteAsync(
            target.FullName,
            "Target.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup></Project>
            """
        );
        await WriteAsync(target.FullName, "Target.cs", source);
        await WriteAsync(
            testDirectory.FullName,
            "Tests.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport><UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner></PropertyGroup>
              <ItemGroup><PackageReference Include="xunit.v3" /><ProjectReference Include="../Target/Target.csproj" /></ItemGroup>
            </Project>
            """
        );
        await WriteAsync(testDirectory.FullName, "Tests.cs", tests);
        var project = FileSystem.Path.Combine(target.FullName, "Target.csproj");
        await RestoreAsync(project);
        return await ExportAsync(project, directory);
    }

    private async Task<CompilationSnapshot> ExportAsync(string project, string directory)
    {
        var path = FileSystem.Path.Combine(directory, "snapshot.json");
        var exported = await RunProcessAsync(
            "dotnet",
            [GetOutputPath("DrillPress.BuildHost"), "export", project, path],
            directory,
            TestContext.Current.CancellationToken
        );
        Assert.True(exported.ExitCode == 0, exported.StandardOutput + exported.StandardError);
        return await new CompilationSnapshotFile().ReadAsync(
            path,
            TestContext.Current.CancellationToken
        );
    }

    private Task WriteAsync(string directory, string name, string text) =>
        FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(directory, name),
            text,
            TestContext.Current.CancellationToken
        );
}
