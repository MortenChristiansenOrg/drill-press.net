using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Rules;

public sealed class RuleDefinitionTests
{
    [Fact]
    public void Typed_clauses_share_the_exact_descriptor_and_return_the_rule()
    {
        var rules = new RuleSet();
        var descriptor = new RuleDescriptor("SHARED", "Use descriptive names.")
        {
            FixComplexity = RuleFixComplexity.Local,
        };
        var definition = rules.Rule(descriptor);
        var solution = RuleTestData.Solution(("A.cs", ["A", "Long"]));

        var returned = definition
            .For(Code.MemberReferences.Where(reference => reference.MemberName == "A"))
            .Forbid()
            .For(Code.MemberReferences.Select(reference => reference.Expression))
            .Require(expression => expression.Location.Length > "Sample.Target.A".Length);
        var diagnostics = rules.Evaluate(solution);

        Assert.Same(descriptor, definition.Descriptor);
        Assert.Same(definition, returned);
        Assert.Equal(
            [("SHARED", "A.cs", "Sample.Target.A"), ("SHARED", "A.cs", "Sample.Target.A")],
            diagnostics.Select(RuleTestData.Describe)
        );
    }

    [Theory]
    [InlineData("Same message.")]
    [InlineData("Different message.")]
    public void Another_rule_cannot_reuse_a_reserved_identity(string message)
    {
        var rules = new RuleSet();
        rules.Rule("SHARED", "Same message.");

        var error = Assert.Throws<InvalidOperationException>(() => rules.Rule("SHARED", message));

        Assert.Equal("Rule id 'SHARED' is registered more than once.", error.Message);
    }

    [Fact]
    public void A_descriptor_cannot_reuse_a_reserved_identity()
    {
        var rules = new RuleSet();
        rules.Rule("SHARED", "Same message.").For(Code.MemberReferences).Forbid();

        var error = Assert.Throws<InvalidOperationException>(() =>
            rules.Rule(new RuleDescriptor("SHARED", "Same message."))
        );

        Assert.Equal("Rule id 'SHARED' is registered more than once.", error.Message);
    }

    [Fact]
    public void Invalid_shared_fix_effort_is_rejected_before_any_clause_is_registered()
    {
        var rules = new RuleSet();

        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            rules.Rule("SHARED", "Message.", (RuleFixComplexity)99)
        );

        Assert.Equal("descriptor", error.ParamName);
    }
}
