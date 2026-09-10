using DrillPress.Manifest;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class MemberCandidateIndexTests(SemanticRuleFixture fixture) : IClassFixture<SemanticRuleFixture>
{
    [Fact]
    public void Compositions_and_unfiltered_queries_preserve_complete_ordered_results()
    {
        var project = fixture.Project("""
            using Text = System.String;
            class C
            {
                string Value => Text.@Empty;
                int Size => Value.Length;
                string Copy() => Value.ToString();
            }
            """);
        var empty = Members.Are<string>("Empty");
        var length = Members.Are<string>("Length");
        var unknown = new RuleCondition<MemberReference>(reference => reference.MemberName == "Value");
        var rules = new RuleSet();
        rules.For(Code.MemberReferences.Where(empty)).Forbid("A", "Empty");
        rules.For(Code.MemberReferences.Where(empty.Or(length))).Forbid("B", "Union");
        rules.For(Code.MemberReferences.Where(empty.And(length))).Forbid("C", "Intersection");
        rules.For(Code.MemberReferences.Where(empty.Or(unknown))).Forbid("D", "Unrestricted alternative");
        rules.For(Code.MemberReferences.Where(empty.Not())).Forbid("E", "Complement");
        rules.For(Code.MemberReferences.Where(empty.Or(length).ExceptWhen(length))).Forbid("F", "Exception");
        rules.For(Code.MemberReferences.Where(empty.Or(length)).Where(length)).Forbid("G", "Chained intersection");
        rules.For(Code.MemberReferences.ExceptWhen(empty)).Forbid("H", "Query exception");
        rules.For(Code.MemberReferences).Forbid("I", "All references after filtered queries");
        var exhaustive = new AnalysisSolution([project], new AnalysisOptions { EnableOptimizations = false });
        var optimized = new AnalysisSolution([project]);

        var expected = rules.Evaluate(exhaustive);
        var actual = rules.Evaluate(optimized);

        Assert.Equal(expected.Select(Describe), actual.Select(Describe));
        Assert.Equal(exhaustive.MemberReferences.Select(DescribeReference), optimized.MemberReferences.Select(DescribeReference));
        Assert.Equal(["Text.@Empty"], actual.Where(item => item.Descriptor.Id == "A").Select(item => project.Sources[0].Document.Text.Substring(item.Location.Start, item.Location.Length)));
        Assert.DoesNotContain(actual, item => item.Descriptor.Id == "C");
    }

    [Fact]
    public void Name_constraints_bind_only_matching_names_and_share_bindings_between_rules()
    {
        var project = fixture.Project("class C { string Value => string.Empty; int Size => Value.Length; }");
        using var writer = new StringWriter();
        var profile = new PipelineProfile(true, writer, "rules");
        var solution = new AnalysisSolution([project], new AnalysisOptions { Profile = profile });
        var rules = new RuleSet();
        rules.For(Code.MemberReferences.Where(Members.Are<string>("Empty"))).Forbid("A", "First");
        rules.For(Code.MemberReferences.Where(Members.Are<string>("Empty"))).Forbid("B", "Second");

        var diagnostics = rules.Evaluate(solution);

        Assert.Equal(["A", "B"], diagnostics.Select(diagnostic => diagnostic.Descriptor.Id));
        Assert.Equal(1, ReadBindingCount(writer.ToString()));
    }

    private static long? ReadBindingCount(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => System.Text.Json.JsonSerializer.Deserialize(line["drillpress-profile ".Length..],
            CompilationSnapshotJsonContext.Default.ProfileEvent)!)
        .Single(entry => entry.Phase == "member.symbol.bindings").Count;

    private static object Describe(RuleDiagnostic diagnostic) => (diagnostic.Descriptor, diagnostic.Location);

    private static object DescribeReference(MemberReference reference) => (reference.ContainingType, reference.MemberName, reference.Location);
}
