using System.Text.Json;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Rules;

public sealed class RuleProfilingTests(SemanticRuleFixture fixture)
    : IClassFixture<SemanticRuleFixture>
{
    [Fact]
    public void Only_the_first_rule_to_bind_calls_pays_for_the_shared_binding()
    {
        var project = fixture.Project("class P { void M() => M(); void N() => N(); }");
        var rules = new RuleCatalog();
        rules.Rule("A", "M.").For(Code.Calls.Where(call => call.Target.Name == "M")).Forbid();
        rules.Rule("B", "N.").For(Code.Calls.Where(call => call.Target.Name == "N")).Forbid();
        using var writer = new StringWriter();
        var solution = new AnalysisSolution(
            [project],
            new AnalysisOptions { Profile = new PipelineProfile(true, writer, "rules") }
        );

        rules.Evaluate(solution);

        var events = Events(writer.ToString());
        Assert.Equal(
            ["rule.A", "rule.A.own", "rule.B", "rule.B.own", "shared.operations"],
            events.Keys
        );
        Assert.Equal(events["shared.operations"], events["rule.A"] - events["rule.A.own"], 6);
        Assert.Equal(events["rule.B"], events["rule.B.own"]);
    }

    private static Dictionary<string, double> Events(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
                JsonSerializer.Deserialize(
                    line["drillpress-profile ".Length..],
                    CompilationSnapshotJsonContext.Default.ProfileEvent
                )!
            )
            .Where(entry => entry.Count is null)
            .ToDictionary(entry => entry.Phase, entry => entry.WallMilliseconds);
}
