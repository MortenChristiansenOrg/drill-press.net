using DrillPress.Engine;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Testing;
using Xunit;

namespace DrillPress.IntegrationTests.Engine.Coverage;

public sealed class CoverageCancellationTests : IntegrationTest
{
    [Fact]
    public async Task Cancellation_stops_external_collection_work_before_returning()
    {
        var root = CreateTemporaryDirectory("drillpress-coverage-cancellation-");
        var directory = FileSystem.Path.Combine(root.FullName, "collection");
        FileSystem.Directory.CreateDirectory(directory);
        var readyPath = FileSystem.Path.Combine(directory, "ready");
        var targetPath = FileSystem.Path.Combine(directory, "Target.csproj");
        await FileSystem.File.WriteAllTextAsync(
            targetPath,
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
                ProjectPath = targetPath,
            },
            project.Compilation
        );
        var processRunner = new BlockingCoverageProcess(
            GetOutputPath("DrillPress.TestProcess", "tests"),
            readyPath
        );
        var rules = new RuleSet();
        rules
            .For(Code.Files)
            .Require(global::DrillPress.Coverage.Line.AtLeast(90), "COV001", "Exercise file.");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var run = new AnalysisEngine(FileSystem, processRunner).EvaluateAsync(
            rules,
            "cancellation",
            [context],
            cancellation.Token
        );
        var process = await WaitForTestProcessAsync(readyPath, run, cancellation);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        FileSystem.Directory.Delete(directory, recursive: true);

        Assert.True(process.HasExited);
        Assert.False(FileSystem.Directory.Exists(directory));
    }
}
