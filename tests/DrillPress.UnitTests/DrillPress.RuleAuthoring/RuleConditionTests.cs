using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring;

public sealed class RuleConditionTests
{
    [Fact]
    public void Predicate_controls_candidates_when_used_by_a_query()
    {
        var condition = new RuleCondition<MemberReference>(reference => reference.Location.Start > 0);
        var query = Code.MemberReferences.Where(condition);

        var diagnostics = RuleTestData.Evaluate(
            query,
            RuleTestData.Reference<string>("First"),
            RuleTestData.Reference<string>("Second", start: 10));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(10, diagnostic.Location.Start);
    }
}
