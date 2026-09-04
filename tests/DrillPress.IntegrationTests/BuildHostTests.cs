using DrillPress.Engine;
using DrillPress.Manifest;
using DrillPress.SampleRules;
using Xunit;

namespace DrillPress.IntegrationTests;

public sealed class BuildHostTests : IntegrationTest
{
    [Fact]
    public async Task Exports_the_sample_project_to_the_requested_snapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var buildHost = GetOutputPath("DrillPress.BuildHost");
        var temporaryDirectory = CreateTemporaryDirectory("drillpress-buildhost-");
        var snapshotPath = Path.Combine(temporaryDirectory.FullName, "requested.snapshot.json");

        var result = await RunProcessAsync(
            "dotnet",
            [buildHost, "export", SampleProjectPath, snapshotPath],
            RepositoryRoot,
            cancellationToken);
        var snapshot = await CompilationSnapshot.ReadAsync(snapshotPath, cancellationToken);
        var diagnostics = await AnalysisEngine.AnalyzeAsync(
            SampleRuleSet.Create(), snapshotPath, cancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
        Assert.True(File.Exists(snapshotPath));
        var project = Assert.Single(snapshot.Projects);
        Assert.Equal("WidgetLibrary", project.Name);
        Assert.Equal(SampleProjectPath, project.ProjectPath);
        Assert.Contains(project.Documents, document =>
            document.Path.EndsWith("Contracts.cs") && !document.IsGenerated);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DP1004", diagnostic.Descriptor.Id);
        Assert.EndsWith("Contracts.cs", diagnostic.Location.FilePath);
        Assert.Equal(10, diagnostic.Location.Line);
        Assert.Equal(29, diagnostic.Location.Column);
    }
}
