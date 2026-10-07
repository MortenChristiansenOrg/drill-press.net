using Xunit;

namespace DrillPress.UnitTests.Testing;

public sealed class EnumerationCoverageTests
{
    private const string Source = """
        namespace System {
            public class Object {} public class ValueType {} public struct Void {}
            public struct Boolean {} public struct Int32 {} public interface IDisposable { void Dispose(); }
        }
        class Sequence { public Enumerator GetEnumerator() => new(); }
        sealed class Enumerator { public bool MoveNext() => false; public int Current => 0; }
        class C {
            static Sequence Make() => new();
            void M() {
                foreach (var item in Make()) { }
                foreach (var item in Make()) { }
                foreach (var item in Make()) { }
                foreach (var item in Make()) { }
            }
        }
        """;

    [Fact]
    public async Task Advancement_facts_are_independent_of_collection_execution_and_missing_facts_remain_unknown()
    {
        var workspace = new global::DrillPress.Testing.RuleTestWorkspace([]);
        workspace.AddProject("Loops", [new("Loops.cs", Source)]);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var loops = Code.Enumerations.In(solution);
        var calls = Code.Calls.ToMethodsNamed("Make").In(solution);
        workspace.WithCoverage(facts =>
        {
            foreach (var call in calls)
                facts.ForCall(call).Executed();
            facts.ForEnumeration(loops[0]).Started();
            facts.ForEnumeration(loops[1]).NotStarted();
            facts.ForEnumeration(loops[2]).Unknown(CoverageReason.UnsupportedEnumerationMapping);
        });
        var rules = new RuleSet();
        rules
            .Rule("ADV", "Exercise enumeration.")
            .For(Code.Enumerations)
            .Require(
                global::DrillPress.Coverage.EnumerationStarted.OnUnknown(
                    "Review advancement mapping."
                )
            );
        rules
            .Rule("COL", "Exercise collection.")
            .For(Code.Enumerations)
            .Require(global::DrillPress.Coverage.Executed);

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(["ADV", "ADV", "ADV"], result.Findings.Select(finding => finding.Rule));
        Assert.Equal(
            [
                "enumeration: uncovered",
                "enumeration: unknown (unsupported-enumeration)",
                "enumeration: unknown (loose-source)",
            ],
            result.Findings.Select(finding => finding.Evidence)
        );
        Assert.Equal(
            [ExecutionCoverage.Uncovered, ExecutionCoverage.Unknown, ExecutionCoverage.Unknown],
            result.Findings.Select(finding => Assert.Single(finding.Coverage).State)
        );
        Assert.All(
            result.Findings,
            finding =>
                Assert.Equal(CoverageMetric.Enumeration, Assert.Single(finding.Coverage).Metric)
        );
        Assert.Equal(
            [null, "Review advancement mapping.", "Review advancement mapping."],
            result.Findings.Select(finding => finding.OutcomeRemediation)
        );
        Assert.Equal(
            loops.Skip(1).Select(loop => loop.SourceExpression.Syntax.ToString()),
            result.Findings.Select(finding => finding.Text)
        );
    }

    [Fact]
    public void Enumeration_review_policy_cannot_bypass_strict_advancement_evidence()
    {
        var requirement = global::DrillPress.Coverage.EnumerationStarted;

        var error = Assert.Throws<ArgumentException>(() =>
            requirement.ReviewUnknownFor(CoverageReason.UnsupportedExpressionMapping)
        );

        Assert.Equal("reasons", error.ParamName);
    }
}
