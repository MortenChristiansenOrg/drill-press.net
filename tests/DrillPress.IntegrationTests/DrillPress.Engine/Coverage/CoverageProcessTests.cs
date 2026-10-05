using DrillPress.Engine;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Testing;
using Xunit;

namespace DrillPress.IntegrationTests.Engine.Coverage;

public sealed class CoverageProcessTests : IntegrationTest
{
    [Theory]
    [InlineData(
        false,
        0,
        "line coverage: unknown (0/0; no-tests, incomplete-lines, zero-coverable-lines)"
    )]
    [InlineData(
        true,
        1,
        "line coverage: unknown (0/0; missing-symbols, incomplete-lines, zero-coverable-lines)"
    )]
    public async Task Large_build_queries_and_successful_test_logs_do_not_fail_coverage(
        bool hasTests,
        int collections,
        string expectedEvidence
    )
    {
        var directory = CreateTemporaryDirectory("drillpress-coverage-output-");
        var target = FileSystem.Path.Combine(directory.FullName, "Target.csproj");
        await FileSystem.File.WriteAllTextAsync(
            target,
            "<Project />",
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(directory.FullName, "Tests.csproj"),
            "<Project />",
            TestContext.Current.CancellationToken
        );
        var project = new RuleTestWorkspace().AddProject(
            "Target",
            [new("Target.cs", "class C { }")]
        );
        var context = new CompilationContext(
            project.Snapshot with
            {
                ProjectPath = target,
            },
            project.Compilation
        );
        var executable = FileSystem.Path.ChangeExtension(
            GetOutputPath("DrillPress.TestProcess", "tests"),
            OperatingSystem.IsWindows() ? ".exe" : null
        );
        var process = new VerboseCoverageProcess(FileSystem, executable, hasTests);
        var cache = new CoverageCache(
            FileSystem,
            CreateTemporaryDirectory("drillpress-coverage-cache-").FullName
        );
        var rules = new RuleSet();
        rules
            .For(Code.Files)
            .Require(global::DrillPress.Coverage.Line.AtLeast(0), "COV001", "Exercise file.");

        var response = await new AnalysisEngine(FileSystem, process, cache).EvaluateAsync(
            rules,
            "large-output",
            [context],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(collections, process.Collections);
        Assert.Equal(collections, process.Installations);
        Assert.Equal(
            expectedEvidence,
            Assert.Single(Assert.Single(response.Contexts).Findings).Evidence
        );
    }
}
