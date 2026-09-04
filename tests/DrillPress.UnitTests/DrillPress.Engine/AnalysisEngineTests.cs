using DrillPress.Engine;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Engine;

public sealed class AnalysisEngineTests : SnapshotTest
{
    [Fact]
    public async Task Finds_only_the_matching_member_reference_at_its_physical_location()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string source = """
            namespace Sample;

            public static class Values
            {
                public static string Violation => string.Empty;
                public static string Compliant => "";
            }
            """;
        var sourcePath = Path.Combine(Path.GetTempPath(), "drillpress-tests", "Values.cs");
        var snapshotPath = await WriteSnapshotAsync(sourcePath, source, cancellationToken);

        var diagnostics = await AnalysisEngine.AnalyzeAsync(
            RuleTestData.StringEmptyRuleSet(), snapshotPath, cancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DP1004", diagnostic.Descriptor.Id);
        Assert.Equal(sourcePath, diagnostic.Location.FilePath);
        Assert.Equal(5, diagnostic.Location.Line);
        Assert.Equal(39, diagnostic.Location.Column);
        Assert.Equal("string.Empty", source.Substring(diagnostic.Location.Start, diagnostic.Location.Length));
    }

    [Fact]
    public async Task Ignores_matching_references_in_generated_documents()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotPath = await WriteSnapshotAsync(
            Path.Combine(Path.GetTempPath(), "drillpress-tests", "Generated.g.cs"),
            "public static class Generated { public static string Value => string.Empty; }",
            cancellationToken,
            isGenerated: true);

        var diagnostics = await AnalysisEngine.AnalyzeAsync(
            RuleTestData.StringEmptyRuleSet(), snapshotPath, cancellationToken);

        Assert.Empty(diagnostics);
    }
}
