using System.Xml.Linq;
using DrillPress.Engine;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.IntegrationTests.Engine.Coverage;

public sealed class CoverageCollectorTests : IntegrationTest
{
    [Fact]
    public async Task Collects_real_tests_without_collector_configuration_and_reuses_the_report()
    {
        var directory = CreateTemporaryDirectory("drillpress-coverage-test-");
        var targetDirectory = FileSystem.Path.Combine(directory.FullName, "Target");
        var testDirectory = FileSystem.Path.Combine(directory.FullName, "Tests");
        FileSystem.Directory.CreateDirectory(targetDirectory);
        FileSystem.Directory.CreateDirectory(testDirectory);
        var targetProject = FileSystem.Path.Combine(targetDirectory, "Target.csproj");
        var testProject = FileSystem.Path.Combine(testDirectory, "Tests.csproj");
        var secondTestDirectory = FileSystem.Path.Combine(directory.FullName, "OtherTests");
        FileSystem.Directory.CreateDirectory(secondTestDirectory);
        var sourcePath = FileSystem.Path.Combine(targetDirectory, "Target.cs");
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(directory.FullName, "Workspace.slnx"),
            "<Solution />",
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(directory.FullName, "global.json"),
            FileSystem.File.ReadAllText(RepositoryPath("global.json")),
            TestContext.Current.CancellationToken
        );
        var packages = new XDocument(
            new XElement(
                "Project",
                new XElement(
                    "Import",
                    new XAttribute("Project", RepositoryPath("Directory.Packages.props"))
                )
            )
        );
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(directory.FullName, "Directory.Packages.props"),
            packages.ToString(),
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            targetProject,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup>
            </Project>
            """,
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            testProject,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>
                <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
                <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
              </PropertyGroup>
              <ItemGroup><PackageReference Include="xunit.v3" /><ProjectReference Include="../Target/Target.csproj" /></ItemGroup>
            </Project>
            """,
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(secondTestDirectory, "OtherTests.csproj"),
            FileSystem.File.ReadAllText(testProject),
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(secondTestDirectory, "Tests.cs"),
            "public class MissTests { [Xunit.Fact] public void Miss() { Xunit.Assert.Equal(\"hit\", Target.Run(null)); Xunit.Assert.Equal(1, Extra.Value); } }",
            TestContext.Current.CancellationToken
        );
        const string source = """
            public static class Target
            {
                public static void Tick() { }
                public static string Hit() => "hit";
                public static System.Threading.Tasks.Task<string> LoadAsync() => System.Threading.Tasks.Task.FromResult("loaded");
                public static async System.Threading.Tasks.Task<string> Awaited() => await (LoadAsync());
                public static string Run(string? cached)
                {
                    Tick();
                    return cached ?? Hit();
                }
                public static string Throwing() => throw new System.InvalidOperationException();
                public static void Consume(string value) { }
                public static void ArgumentThrows() { Consume(Throwing()); }
                public static void ReceiverThrows() { Throwing().ToString(); }
                public static void Never()
                {
                    Tick();
                }
            }
            """;
        await FileSystem.File.WriteAllTextAsync(
            sourcePath,
            source,
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(targetDirectory, "Extra.cs"),
            "public static class Extra { public static int Value => 1; }",
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(testDirectory, "Tests.cs"),
            """
            public class CacheTests
            {
                [Xunit.Fact]
                public void UsesCache() => Xunit.Assert.Equal("cached", Target.Run("cached"));
                [Xunit.Fact]
                public void ArgumentFails() => Xunit.Assert.Throws<System.InvalidOperationException>(Target.ArgumentThrows);
                [Xunit.Fact]
                public void ReceiverFails() => Xunit.Assert.Throws<System.InvalidOperationException>(Target.ReceiverThrows);
                [Xunit.Fact]
                public async System.Threading.Tasks.Task Awaits() => Xunit.Assert.Equal("loaded", await Target.Awaited());
            }
            """,
            TestContext.Current.CancellationToken
        );
        await RestoreAsync(targetProject);
        var snapshotPath = FileSystem.Path.Combine(directory.FullName, "snapshot.json");
        var exported = await RunProcessAsync(
            "dotnet",
            [GetOutputPath("DrillPress.BuildHost"), "export", targetProject, snapshotPath],
            directory.FullName,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(0, exported.ExitCode);
        var snapshot = await new CompilationSnapshotFile().ReadAsync(
            snapshotPath,
            TestContext.Current.CancellationToken
        );
        var process = new CountingCoverageProcess();
        var engine = new AnalysisEngine(FileSystem, process);
        var rules = new RuleSet();
        rules
            .Rule("COV001", "Exercise call.")
            .For(
                Code.Calls.Where(call =>
                    call.Target.Name is "Tick" or "Hit" or "Consume" or "ToString" or "LoadAsync"
                )
            )
            .Require(global::DrillPress.Coverage.Executed);

        rules
            .Rule("COV002", "Exercise both branches.")
            .For(
                Code.Nodes<Microsoft.CodeAnalysis.CSharp.Syntax.ReturnStatementSyntax>()
                    .Where(node => node.Syntax.ToString() == "return cached ?? Hit();")
            )
            .Require(global::DrillPress.Coverage.Line.AtLeast(100));

        rules
            .Rule("COV003", "Exercise project.")
            .For(Code.Projects)
            .Require(global::DrillPress.Coverage.Line.AtLeast(70));

        var simultaneous = await Task.WhenAll(
            engine.AnalyzeAsync(rules, snapshot, TestContext.Current.CancellationToken),
            engine.AnalyzeAsync(rules, snapshot, TestContext.Current.CancellationToken)
        );
        var first = simultaneous[0];
        var second = await engine.AnalyzeAsync(
            rules,
            snapshot,
            TestContext.Current.CancellationToken
        );

        Assert.Equivalent(first, simultaneous[1], strict: true);
        Assert.Equivalent(first, second, strict: true);
        Assert.Equal(2, process.Collections);
        Assert.Equal(
            ["Consume(Throwing())", "Throwing().ToString()", "Tick()"],
            first
                .Where(diagnostic => diagnostic.Descriptor.Id == "COV001")
                .Select(diagnostic =>
                    source.Substring(diagnostic.Location.Start, diagnostic.Location.Length)
                )
        );
        Assert.Equal(
            ["coverage: uncovered", "coverage: uncovered", "coverage: uncovered"],
            first
                .Where(diagnostic => diagnostic.Descriptor.Id == "COV001")
                .Select(diagnostic => diagnostic.Evidence)
        );
        Assert.Equal(
            "line coverage: 62.5% (10/16), required 70%",
            Assert.Single(first, diagnostic => diagnostic.Descriptor.Id == "COV003").Evidence
        );
    }
}
