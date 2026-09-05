using DrillPress.Engine;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Engine;

public sealed class AnalysisEngineTests
{
    [Fact]
    public async Task Finds_only_the_matching_member_reference_at_its_physical_location()
    {
        const string source = """
            namespace Sample;

            public sealed class Target
            {
                public static Target Empty => null;
            }

            public static class Values
            {
                public static Target Violation => Target.Empty;
                public static Target Compliant => null;
            }
            """;
        var snapshot = TestSnapshots.Create(source, "Values.cs");

        var diagnostics = await AnalysisEngine.AnalyzeAsync(
            RuleTestData.TargetEmptyRuleSet(),
            snapshot,
            TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("TEST001", diagnostic.Descriptor.Id);
        Assert.Equal("Values.cs", diagnostic.Location.FilePath);
        Assert.Equal(10, diagnostic.Location.Line);
        Assert.Equal(39, diagnostic.Location.Column);
        Assert.Equal("Target.Empty", source.Substring(diagnostic.Location.Start, diagnostic.Location.Length));
    }

    [Fact]
    public async Task Ignores_matching_references_in_generated_documents()
    {
        var snapshot = TestSnapshots.Create(
            """
            namespace Sample;
            public sealed class Target
            {
                public static Target Empty => null;
            }
            public static class Generated
            {
                public static Target Value => Target.Empty;
            }
            """,
            "Generated.g.cs",
            isGenerated: true);

        var diagnostics = await AnalysisEngine.AnalyzeAsync(
            RuleTestData.TargetEmptyRuleSet(),
            snapshot,
            TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }
}
