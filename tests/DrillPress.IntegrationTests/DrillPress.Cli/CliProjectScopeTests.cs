using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.Cli;

public sealed class CliProjectScopeTests : IntegrationTest
{
    private const string DependencySource =
        "public class Dependency { public static string Value => string.Empty; }";
    private const string LeafSource =
        "public class Leaf { public static string Value => string.Empty; }";

    [Theory]
    [InlineData("App/App.csproj")]
    [InlineData("App")]
    public async Task Project_targets_exclude_direct_and_transitive_dependency_findings(
        string target
    )
    {
        var root = await CreateGraphAsync();

        var result = await RunCliAsync(
            FileSystem.Path.Combine(root, target),
            "--validate-compilation"
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.Equal("", result.StandardError);
    }

    [Theory]
    [InlineData("App/App.csproj", new[] { "--include-referenced-projects" })]
    [InlineData("App", new[] { "--include-referenced-projects", "--no-optimization" })]
    [InlineData("All.slnx", new string[0])]
    public async Task Opt_in_and_solution_targets_report_dependency_findings(
        string target,
        string[] options
    )
    {
        var root = await CreateGraphAsync();
        var dependencyPath = FileSystem
            .Path.GetRelativePath(
                RepositoryRoot,
                FileSystem.Path.Combine(root, "Dependency/Source.cs")
            )
            .Replace('\\', '/');
        var leafPath = FileSystem
            .Path.GetRelativePath(RepositoryRoot, FileSystem.Path.Combine(root, "Leaf/Source.cs"))
            .Replace('\\', '/');

        var result = await RunCliAsync(FileSystem.Path.Combine(root, target), options);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.StandardError);
        Assert.Equal(
            $"""
            DP1004 Use the empty string literal "" instead of string.Empty.
            {dependencyPath}
              +1:57
            {leafPath}
              +1:51

            """.ReplaceLineEndings("\n"),
            result.StandardOutput
        );
    }

    [Theory]
    [InlineData(new string[0], DependencySource, LeafSource)]
    [InlineData(
        new[] { "--include-referenced-projects" },
        "public class Dependency { public static string Value => \"\"; }",
        "public class Leaf { public static string Value => \"\"; }"
    )]
    public async Task Fix_respects_project_scope(
        string[] options,
        string expectedDependency,
        string expectedLeaf
    )
    {
        var root = await CreateGraphAsync();
        await WriteAsync(root, "App/Source.cs", "class App { string Value => string.Empty; }");

        var result = await RunProcessAsync(
            "dotnet",
            [
                GetOutputPath("DrillPress.Cli"),
                "fix",
                "--build-host",
                GetOutputPath("DrillPress.BuildHost"),
                "--rules",
                GetOutputPath("DrillPress.SampleRules", "samples"),
                FileSystem.Path.Combine(root, "App/App.csproj"),
                .. options,
            ],
            RepositoryRoot,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(new ProcessResult(0, "", ""), result);
        Assert.Equal("class App { string Value => \"\"; }", await ReadAsync(root, "App/Source.cs"));
        Assert.Equal(expectedDependency, await ReadAsync(root, "Dependency/Source.cs"));
        Assert.Equal(expectedLeaf, await ReadAsync(root, "Leaf/Source.cs"));
    }

    [Fact]
    public async Task Every_selected_framework_is_in_scope_while_dependencies_remain_available()
    {
        var root = await CreateGraphAsync("net10.0;netstandard2.1", "netstandard2.1");
        var output = FileSystem.Path.Combine(root, "snapshot.json");

        var result = await RunProcessAsync(
            "dotnet",
            [
                GetOutputPath("DrillPress.BuildHost"),
                "export",
                FileSystem.Path.Combine(root, "App/App.csproj"),
                output,
                "--validate-compilation",
            ],
            RepositoryRoot,
            TestContext.Current.CancellationToken
        );
        var snapshot = await new CompilationSnapshotFile().ReadAsync(
            output,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(new ProcessResult(0, "", ""), result);
        Assert.Equal(
            ["net10.0", "netstandard2.1"],
            snapshot
                .Projects.Where(project => project.IsAnalysisTarget)
                .Select(project => project.TargetFramework)
        );
        Assert.Equal(
            ["Dependency", "Leaf"],
            snapshot
                .Projects.Where(project => !project.IsAnalysisTarget)
                .Select(project => project.Name)
        );
    }

    private async Task<string> CreateGraphAsync(
        string frameworks = "net10.0",
        string dependencyFramework = "net10.0"
    )
    {
        var root = CreateTemporaryDirectory("drillpress-project-scope-").FullName;
        foreach (var name in new[] { "App", "Dependency", "Leaf" })
        {
            FileSystem.Directory.CreateDirectory(FileSystem.Path.Combine(root, name));
        }
        await WriteAsync(
            root,
            "Leaf/Leaf.csproj",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>{{dependencyFramework}}</TargetFramework></PropertyGroup>
            </Project>
            """
        );
        await WriteAsync(
            root,
            "Dependency/Dependency.csproj",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>{{dependencyFramework}}</TargetFramework></PropertyGroup>
              <ItemGroup><ProjectReference Include="../Leaf/Leaf.csproj" /></ItemGroup>
            </Project>
            """
        );
        await WriteAsync(
            root,
            "App/App.csproj",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFrameworks>{{frameworks}}</TargetFrameworks></PropertyGroup>
              <ItemGroup><ProjectReference Include="../Dependency/Dependency.csproj" /></ItemGroup>
            </Project>
            """
        );
        await WriteAsync(root, "Leaf/Source.cs", LeafSource);
        await WriteAsync(root, "Dependency/Source.cs", DependencySource);
        await WriteAsync(
            root,
            "App/Source.cs",
            "class App { string Value => Dependency.Value + Leaf.Value; }"
        );
        await WriteAsync(
            root,
            "All.slnx",
            "<Solution><Project Path=\"App/App.csproj\" /></Solution>"
        );
        await RestoreAsync(FileSystem.Path.Combine(root, "All.slnx"));
        return root;
    }

    private static Task WriteAsync(string root, string path, string text) =>
        FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(root, path),
            text,
            TestContext.Current.CancellationToken
        );

    private static Task<string> ReadAsync(string root, string path) =>
        FileSystem.File.ReadAllTextAsync(
            FileSystem.Path.Combine(root, path),
            TestContext.Current.CancellationToken
        );
}
