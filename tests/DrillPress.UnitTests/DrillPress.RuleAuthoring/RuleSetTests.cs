using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring;

public sealed class RuleSetTests
{
    [Fact]
    public void Evaluate_orders_diagnostics_by_rule_path_and_source_position()
    {
        var rules = new RuleSet();
        rules.For(Code.MemberReferences).Forbid("Z002", "Later rule.");
        rules.For(Code.MemberReferences).Forbid("A001", "Earlier rule.");
        var references = new[]
        {
            RuleTestData.Reference<string>("Second", "B.cs", 20),
            RuleTestData.Reference<string>("First", "A.cs", 10),
        };

        var diagnostics = rules.Evaluate(references);

        Assert.Collection(
            diagnostics,
            diagnostic => Assert.Equal(("A001", "A.cs", 10), Describe(diagnostic)),
            diagnostic => Assert.Equal(("A001", "B.cs", 20), Describe(diagnostic)),
            diagnostic => Assert.Equal(("Z002", "A.cs", 10), Describe(diagnostic)),
            diagnostic => Assert.Equal(("Z002", "B.cs", 20), Describe(diagnostic)));
    }

    [Fact]
    public void Forbid_rejects_duplicate_rule_ids()
    {
        var rules = new RuleSet();
        rules.For(Code.MemberReferences).Forbid("TEST001", "First message.");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            rules.For(Code.MemberReferences).Forbid("TEST001", "Second message."));

        Assert.Contains("registered more than once", exception.Message);
    }

    [Theory]
    [InlineData("", "Message")]
    [InlineData("TEST001", "")]
    public void Forbid_rejects_blank_rule_identity_or_message(string id, string message)
    {
        var rules = new RuleSet();

        Assert.Throws<ArgumentException>(() =>
            rules.For(Code.MemberReferences).Forbid(id, message));
    }

    private static (string Id, string Path, int Start) Describe(RuleDiagnostic diagnostic) =>
        (diagnostic.Descriptor.Id, diagnostic.Location.FilePath, diagnostic.Location.Start);
}
