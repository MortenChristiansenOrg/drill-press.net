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
                FileSystem.Path.Combine(root, "Dependency/Dependency.cs")
            )
            .Replace('\\', '/');
        var leafPath = FileSystem
            .Path.GetRelativePath(RepositoryRoot, FileSystem.Path.Combine(root, "Leaf/Leaf.cs"))
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
        await WriteAsync(root, "App/App.cs", "class App { string Value => string.Empty; }");

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
        Assert.Equal("class App { string Value => \"\"; }", await ReadAsync(root, "App/App.cs"));
        Assert.Equal(expectedDependency, await ReadAsync(root, "Dependency/Dependency.cs"));
        Assert.Equal(expectedLeaf, await ReadAsync(root, "Leaf/Leaf.cs"));
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

    [Fact]
    public async Task Several_targets_keep_their_own_scope_and_report_shared_files_once()
    {
        var root = await CreateGraphAsync();

        var single = await RunCliAsync(FileSystem.Path.Combine(root, "All.slnx"));
        var several = await RunCliAsync(
            FileSystem.Path.Combine(root, "App/App.csproj"),
            FileSystem.Path.Combine(root, "All.slnx"),
            FileSystem.Path.Combine(root, "Leaf/Leaf.cs"),
            FileSystem.Path.Combine(root, "Leaf"),
            "--validate-compilation"
        );

        Assert.Equal(1, single.ExitCode);
        Assert.Equal(
            (single.ExitCode, single.StandardOutput, single.StandardError),
            (several.ExitCode, several.StandardOutput, several.StandardError)
        );
    }

    [Fact]
    public async Task A_solution_beside_other_targets_keeps_its_configuration_mapping()
    {
        var root = await CreateGraphAsync();
        await WriteAsync(
            root,
            "App/App.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><ProjectReference Include="../Dependency/Dependency.csproj" /></ItemGroup>
              <ItemGroup Condition="'$(Configuration)' != 'Release'"><Compile Remove="ReleaseOnly.cs" /></ItemGroup>
            </Project>
            """
        );
        await WriteAsync(
            root,
            "App/ReleaseOnly.cs",
            "class ReleaseOnly { string Value => string.Empty; }"
        );
        await WriteAsync(
            root,
            "Retail.slnx",
            """
            <Solution>
              <Configurations><BuildType Name="Retail" /></Configurations>
              <Project Path="App/App.csproj"><BuildType Solution="Retail|*" Project="Release" /></Project>
            </Solution>
            """
        );

        var single = await RunCliAsync(FileSystem.Path.Combine(root, "Retail.slnx"));
        var several = await RunCliAsync(
            FileSystem.Path.Combine(root, "Retail.slnx"),
            FileSystem.Path.Combine(root, "Leaf/Leaf.csproj")
        );

        Assert.Equal(
            (
                1,
                $"""
                DP1004 Use the empty string literal "" instead of string.Empty.
                {Relative(root, "App/ReleaseOnly.cs")}
                  +1:37
                {Relative(root, "Dependency/Dependency.cs")}
                  +1:57
                {Relative(root, "Leaf/Leaf.cs")}
                  +1:51

                """.ReplaceLineEndings("\n"),
                ""
            ),
            (single.ExitCode, single.StandardOutput, single.StandardError)
        );
        Assert.Equal(
            (single.ExitCode, single.StandardOutput, single.StandardError),
            (several.ExitCode, several.StandardOutput, several.StandardError)
        );
    }

    [Fact]
    public async Task Solutions_that_build_projects_in_different_configurations_fail_beside_other_targets()
    {
        var root = await CreateGraphAsync();
        await WriteAsync(
            root,
            "Retail.slnx",
            """
            <Solution>
              <Configurations><BuildType Name="Retail" /></Configurations>
              <Project Path="App/App.csproj"><BuildType Solution="Retail|*" Project="Release" /></Project>
              <Project Path="Dependency/Dependency.csproj" />
            </Solution>
            """
        );

        var result = await RunCliAsync(
            FileSystem.Path.Combine(root, "Retail.slnx"),
            FileSystem.Path.Combine(root, "Leaf/Leaf.csproj")
        );

        Assert.Equal(
            (
                2,
                "",
                $"DrillPress.BuildHost: The solutions among the targets build their projects in different configurations (Release|AnyCPU for '{FileSystem.Path.Combine(root, "App", "App.csproj")}', Retail|AnyCPU for '{FileSystem.Path.Combine(root, "Dependency", "Dependency.csproj")}'); check such a solution on its own.{Environment.NewLine}drillpress: BuildHost exited 2.{Environment.NewLine}"
            ),
            (result.ExitCode, result.StandardOutput, result.StandardError)
        );
    }

    [Fact]
    public async Task Project_and_loose_targets_analyse_only_what_they_name()
    {
        var root = await CreateGraphAsync();
        FileSystem.Directory.CreateDirectory(FileSystem.Path.Combine(root, "Scripts"));
        await WriteAsync(
            root,
            "Scripts/Tool.cs",
            "class Tool { static string Value => string.Empty; }"
        );

        var result = await RunCliAsync(
            FileSystem.Path.Combine(root, "App/App.csproj"),
            FileSystem.Path.Combine(root, "Leaf/Leaf.csproj"),
            FileSystem.Path.Combine(root, "Scripts/*.cs"),
            FileSystem.Path.Combine(root, "Dependency/Dependency.cs")
        );

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.StandardError);
        Assert.Equal(
            $"""
            DP1004 Use the empty string literal "" instead of string.Empty.
            {Relative(root, "Dependency/Dependency.cs")}
              +1:57
            {Relative(root, "Leaf/Leaf.cs")}
              +1:51
            {Relative(root, "Scripts/Tool.cs")}
              +1:37

            """.ReplaceLineEndings("\n"),
            result.StandardOutput
        );
    }

    [Fact]
    public async Task Fix_and_status_cover_every_target_in_one_run()
    {
        var root = await CreateGraphAsync();
        string[] targets =
        [
            FileSystem.Path.Combine(root, "Dependency/Dependency.csproj"),
            FileSystem.Path.Combine(root, "Leaf/Leaf.csproj"),
        ];

        var before = await RunCommandAsync("status", targets);
        var fix = await RunCommandAsync("fix", targets);
        var after = await RunCommandAsync("status", targets);

        Assert.Equal(
            new ProcessResult(
                1,
                $"""
                {Relative(root, "Dependency/Dependency.csproj")} 1
                  trivial 1
                {Relative(root, "Leaf/Leaf.csproj")} 1
                  trivial 1

                """.ReplaceLineEndings("\n"),
                ""
            ),
            before
        );
        Assert.Equal(new ProcessResult(0, "", ""), fix);
        Assert.Equal(new ProcessResult(0, "", ""), after);
        Assert.Equal(
            "public class Dependency { public static string Value => \"\"; }",
            await ReadAsync(root, "Dependency/Dependency.cs")
        );
        Assert.Equal(
            "public class Leaf { public static string Value => \"\"; }",
            await ReadAsync(root, "Leaf/Leaf.cs")
        );
    }

    [Fact]
    public async Task Targets_that_select_different_sdks_fail_before_loading()
    {
        var root = await CreateGraphAsync();
        await WriteAsync(
            root,
            "Leaf/global.json",
            $$$"""{"sdk":{"version":"{{{Environment.Version.Major}}}.0.100","rollForward":"latestFeature"}}"""
        );
        var leafSdk = await SdkVersionAsync(FileSystem.Path.Combine(root, "Leaf"));
        var appSdk = await SdkVersionAsync(FileSystem.Path.Combine(root, "App"));
        Assert.SkipWhen(leafSdk == appSdk, "Only one .NET SDK feature band is installed.");

        var result = await RunCliAsync(
            FileSystem.Path.Combine(root, "App/App.csproj"),
            FileSystem.Path.Combine(root, "Leaf/Leaf.csproj")
        );

        Assert.Equal(
            (
                2,
                "",
                $"DrillPress.BuildHost: The targets select different .NET SDKs ({appSdk} for '{FileSystem.Path.Combine(root, "App")}', {leafSdk} for '{FileSystem.Path.Combine(root, "Leaf")}'); run them in separate invocations.{Environment.NewLine}drillpress: BuildHost exited 2.{Environment.NewLine}"
            ),
            (result.ExitCode, result.StandardOutput, result.StandardError)
        );
    }

    private Task<ProcessResult> RunCommandAsync(string command, string[] targets) =>
        RunProcessAsync(
            "dotnet",
            [
                GetOutputPath("DrillPress.Cli"),
                command,
                "--build-host",
                GetOutputPath("DrillPress.BuildHost"),
                "--rules",
                GetOutputPath("DrillPress.SampleRules", "samples"),
                .. targets,
            ],
            RepositoryRoot,
            TestContext.Current.CancellationToken
        );

    private async Task<string> SdkVersionAsync(string directory) =>
        (
            await RunProcessAsync(
                "dotnet",
                ["--version"],
                directory,
                TestContext.Current.CancellationToken
            )
        ).StandardOutput.Trim();

    private string Relative(string root, string path) =>
        FileSystem
            .Path.GetRelativePath(RepositoryRoot, FileSystem.Path.Combine(root, path))
            .Replace('\\', '/');

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
        await WriteAsync(root, "Leaf/Leaf.cs", LeafSource);
        await WriteAsync(root, "Dependency/Dependency.cs", DependencySource);
        await WriteAsync(
            root,
            "App/App.cs",
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
