using DrillPress.Engine;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.IntegrationTests.Engine.Coverage;

public sealed class EnumerationCoverageTests : IntegrationTest
{
    private const string Source = """
        using System.Collections;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        public static class Loops {
            public static IEnumerable<int> Empty() => System.Array.Empty<int>();
            public static IEnumerable<(int,int)> Pairs() => System.Array.Empty<(int,int)>();
            public static async IAsyncEnumerable<int> AsyncEmpty() { await Task.CompletedTask; yield break; }
            public static void Sync() { foreach (var item in Empty()) { } }
            public static async Task Async() { await foreach (var item in AsyncEmpty()) { } }
            public static void Deconstruct() { foreach (var (a, b) in Pairs()) { } }
            public static IEnumerable<int> FailingCollection() => throw new System.InvalidOperationException();
            public static void CollectionFails() { foreach (var item in FailingCollection()) { } }
            public static void AcquisitionFails() { foreach (var item in (IEnumerable<int>)new Broken()) { } }
            public static void Never() { foreach (var item in Empty()) { } }
            public static async Task NeverAsync() { await foreach (var item in AsyncEmpty()) { } }
            public static void Indexed() { foreach (var item in System.Array.Empty<int>()) { } }
        }
        public sealed class Broken : IEnumerable<int> {
            public IEnumerator<int> GetEnumerator() => throw new System.InvalidOperationException();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
        """;

    [Fact]
    public async Task Empty_sequences_advance_but_collection_acquisition_failures_and_skipped_loops_do_not()
    {
        var directory = CreateTemporaryDirectory("drillpress-enumeration-").FullName;
        var project = await CreateProjectsAsync(directory);
        await RestoreAsync(project);
        var snapshotPath = FileSystem.Path.Combine(directory, "snapshot.json");
        var exported = await RunProcessAsync(
            "dotnet",
            [GetOutputPath("DrillPress.BuildHost"), "export", project, snapshotPath],
            directory,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(0, exported.ExitCode);
        var snapshot = await new CompilationSnapshotFile().ReadAsync(
            snapshotPath,
            TestContext.Current.CancellationToken
        );
        var rules = new RuleSet();
        rules
            .For(Code.Enumerations)
            .Require(global::DrillPress.Coverage.EnumerationStarted, "ENUM", "Start enumeration.");

        var diagnostics = await new AnalysisEngine().AnalyzeAsync(
            rules,
            snapshot,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            [
                "FailingCollection()",
                "(IEnumerable<int>)new Broken()",
                "Empty()",
                "AsyncEmpty()",
                "System.Array.Empty<int>()",
            ],
            diagnostics.Select(diagnostic =>
                Source.Substring(diagnostic.Location.Start, diagnostic.Location.Length)
            )
        );
        Assert.Equal(
            [
                "enumeration: uncovered",
                "enumeration: uncovered",
                "enumeration: uncovered",
                "enumeration: uncovered",
                "enumeration: unknown (unsupported-enumeration)",
            ],
            diagnostics.Select(diagnostic => diagnostic.Evidence)
        );
        Assert.All(
            diagnostics,
            diagnostic =>
                Assert.Equal(CoverageMetric.Enumeration, Assert.Single(diagnostic.Coverage).Metric)
        );
    }

    private async Task<string> CreateProjectsAsync(string directory)
    {
        var target = FileSystem.Directory.CreateDirectory(
            FileSystem.Path.Combine(directory, "Target")
        );
        var tests = FileSystem.Directory.CreateDirectory(
            FileSystem.Path.Combine(directory, "Tests")
        );
        await WriteAsync(directory, "Workspace.slnx", "<Solution />");
        await WriteAsync(
            directory,
            "global.json",
            FileSystem.File.ReadAllText(RepositoryPath("global.json"))
        );
        var packages = new System.Xml.Linq.XElement(
            "Project",
            new System.Xml.Linq.XElement(
                "Import",
                new System.Xml.Linq.XAttribute(
                    "Project",
                    RepositoryPath("Directory.Packages.props")
                )
            )
        );
        await WriteAsync(directory, "Directory.Packages.props", packages.ToString());
        await WriteAsync(
            target.FullName,
            "Target.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>
            """
        );
        await WriteAsync(target.FullName, "Loops.cs", Source);
        await WriteAsync(
            tests.FullName,
            "Tests.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport><UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner></PropertyGroup>
              <ItemGroup><PackageReference Include="xunit.v3" /><ProjectReference Include="../Target/Target.csproj" /></ItemGroup>
            </Project>
            """
        );
        await WriteAsync(
            tests.FullName,
            "Tests.cs",
            """
            public class LoopTests {
                [Xunit.Fact] public async System.Threading.Tasks.Task EmptyLoops() {
                    Loops.Sync(); await Loops.Async(); Loops.Deconstruct(); Loops.Indexed();
                    Xunit.Assert.Throws<System.InvalidOperationException>(Loops.CollectionFails);
                    Xunit.Assert.Throws<System.InvalidOperationException>(Loops.AcquisitionFails);
                }
            }
            """
        );
        return FileSystem.Path.Combine(target.FullName, "Target.csproj");
    }

    private Task WriteAsync(string directory, string name, string text) =>
        FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(directory, name),
            text,
            TestContext.Current.CancellationToken
        );
}
