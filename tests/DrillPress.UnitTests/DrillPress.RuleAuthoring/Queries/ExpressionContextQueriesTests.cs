using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Queries;

public sealed class ExpressionContextQueriesTests
{
    [Fact]
    public void Source_less_references_have_no_facts_and_do_not_pass_context_filters()
    {
        var reference = RuleTestData.Reference<string>(nameof(string.Empty));

        var findings = RuleTestData.Evaluate(Code.MemberReferences.OutsideNameOf(), reference);

        Assert.Null(reference.AsExpression());
        Assert.Null(reference.Facts);
        Assert.Empty(findings);
    }
}
