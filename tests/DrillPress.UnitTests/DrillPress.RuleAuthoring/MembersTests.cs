using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring;

public sealed class MembersTests
{
    [Fact]
    public void Are_matches_both_the_declaring_type_and_member_name()
    {
        var query = Code.MemberReferences.Where(Members.Are<string>(nameof(string.Empty)));
        var references = new[]
        {
            RuleTestData.Reference<string>(nameof(string.Empty)),
            RuleTestData.Reference<string>(nameof(string.Length)),
            RuleTestData.Reference<Uri>(nameof(string.Empty)),
        };

        var diagnostics = RuleTestData.Evaluate(query, references);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(references[0].Location, diagnostic.Location);
    }

    [Fact]
    public void Are_rejects_a_blank_member_name()
    {
        Assert.Throws<ArgumentException>(() => Members.Are<string>(" "));
    }
}
