using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class MemberCandidateIndexTests(SemanticRuleFixture fixture)
    : IClassFixture<SemanticRuleFixture>
{
    [Fact]
    public void Compositions_and_unfiltered_queries_preserve_complete_ordered_results()
    {
        var project = fixture.Project(
            """
            using Text = System.String;
            class C
            {
                string Value => Text.@Empty;
                int Size => Value.Length;
                string Copy() => Value.ToString();
            }
            """
        );
        var empty = CodeType.Of<string>().Member("Empty").References;
        var length = CodeType.Of<string>().Member("Length").References;
        var unknown = Code.MemberReferences.Where(reference => reference.MemberName == "Value");
        var rules = new RuleSet();
        rules.Rule("A", "Empty").For(empty).Forbid();
        rules.Rule("B", "Union").For(empty.Union(length)).Forbid();
        rules
            .Rule("C", "Intersection")
            .For(empty.Where(reference => reference.MemberName == "Length"))
            .Forbid();
        rules.Rule("D", "Unrestricted alternative").For(empty.Union(unknown)).Forbid();
        rules
            .Rule("E", "Complement")
            .For(Code.MemberReferences.ExceptWhen(reference => reference.MemberName == "Empty"))
            .Forbid();
        rules
            .Rule("F", "Exception")
            .For(empty.Union(length).ExceptWhen(reference => reference.MemberName == "Length"))
            .Forbid();
        rules
            .Rule("G", "Chained intersection")
            .For(length.Where(reference => reference.Symbol.ContainingType.Name == "String"))
            .Forbid();
        rules.Rule("H", "Query exception").For(empty.ExceptWhen(_ => true)).Forbid();
        rules
            .Rule("I", "All references after filtered queries")
            .For(Code.MemberReferences)
            .Forbid();
        var exhaustive = new AnalysisSolution(
            [project],
            new AnalysisOptions { EnableOptimizations = false }
        );
        var optimized = new AnalysisSolution([project]);

        var expected = rules.Evaluate(exhaustive);
        var actual = rules.Evaluate(optimized);

        Assert.Equal(expected.Select(Describe), actual.Select(Describe));
        Assert.Equal(
            exhaustive.MemberReferences.Select(DescribeReference),
            optimized.MemberReferences.Select(DescribeReference)
        );
        Assert.Equal(
            ["Text.@Empty"],
            actual
                .Where(item => item.Descriptor.Id == "A")
                .Select(item =>
                    project
                        .Sources[0]
                        .Document.Text.Substring(item.Location.Start, item.Location.Length)
                )
        );
        Assert.DoesNotContain(actual, item => item.Descriptor.Id == "C");
    }

    [Fact]
    public void Name_constraints_bind_only_matching_names_and_share_bindings_between_rules()
    {
        var project = fixture.Project(
            "class C { string Value => string.Empty; int Size => Value.Length; }"
        );
        using var writer = new StringWriter();
        var profile = new PipelineProfile(true, writer, "rules");
        var solution = new AnalysisSolution([project], new AnalysisOptions { Profile = profile });
        var rules = new RuleSet();
        rules.Rule("A", "First").For(CodeType.Of<string>().Member("Empty").References).Forbid();
        rules.Rule("B", "Second").For(CodeType.Of<string>().Member("Empty").References).Forbid();

        var diagnostics = rules.Evaluate(solution);

        Assert.Equal(["A", "B"], diagnostics.Select(diagnostic => diagnostic.Descriptor.Id));
        Assert.Equal(1, ReadBindingCount(writer.ToString()));
    }

    private static long? ReadBindingCount(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
                System.Text.Json.JsonSerializer.Deserialize(
                    line["drillpress-profile ".Length..],
                    CompilationSnapshotJsonContext.Default.ProfileEvent
                )!
            )
            .Single(entry => entry.Phase == "member.symbol.bindings")
            .Count;

    private static object Describe(RuleDiagnostic diagnostic) =>
        (diagnostic.Descriptor, diagnostic.Location);

    private static object DescribeReference(MemberReference reference) =>
        (reference.ContainingType, reference.MemberName, reference.Location);
}
