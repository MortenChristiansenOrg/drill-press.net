using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Rules;

public sealed class RuleDefinitionTests
{
    [Fact]
    public void Typed_clauses_share_the_exact_descriptor_and_preserve_fluent_registration()
    {
        var rules = new RuleSet();
        var descriptor = new RuleDescriptor("SHARED", "Use descriptive names.")
        {
            FixComplexity = RuleFixComplexity.Local,
        };
        var definition = rules.Rule(descriptor);
        var forbidden = definition.For(
            Code.MemberReferences.Where(reference => reference.MemberName == "A")
        );
        var required = definition.For(
            Code.MemberReferences.Select(reference => reference.Location)
        );
        var references = new[]
        {
            RuleTestData.Reference<string>("A", start: 0),
            RuleTestData.Reference<string>("Long", start: 10),
        };

        var returnedForbidden = forbidden.Forbid();
        var returnedRequired = required.Require(
            location => location.Length > 1,
            location: location => location
        );
        var diagnostics = rules.Evaluate(references);

        Assert.Same(descriptor, definition.Descriptor);
        Assert.Same(forbidden, returnedForbidden);
        Assert.Same(required, returnedRequired);
        Assert.Equal(
            new[]
            {
                new RuleDiagnostic(descriptor, references[0].Location),
                new RuleDiagnostic(descriptor, references[0].Location),
            },
            diagnostics
        );
    }

    [Theory]
    [InlineData("Same message.")]
    [InlineData("Different message.")]
    public void Another_definition_cannot_reuse_a_reserved_identity(string message)
    {
        var rules = new RuleSet();
        rules.Rule("SHARED", "Same message.");

        var error = Assert.Throws<InvalidOperationException>(() => rules.Rule("SHARED", message));

        Assert.Equal("Rule id 'SHARED' is registered more than once.", error.Message);
    }

    [Fact]
    public void Ordinary_registration_cannot_reuse_a_reserved_identity_even_without_clauses()
    {
        var rules = new RuleSet();
        rules.Rule("SHARED", "Same message.");

        var error = Assert.Throws<InvalidOperationException>(() =>
            rules.For(Code.MemberReferences).Forbid(new RuleDescriptor("SHARED", "Same message."))
        );

        Assert.Equal("Rule id 'SHARED' is registered more than once.", error.Message);
    }

    [Fact]
    public void A_definition_cannot_claim_an_ordinary_registration()
    {
        var rules = new RuleSet();
        rules.For(Code.MemberReferences).Forbid("SHARED", "Same message.");

        var error = Assert.Throws<InvalidOperationException>(() =>
            rules.Rule("SHARED", "Same message.")
        );

        Assert.Equal("Rule id 'SHARED' is registered more than once.", error.Message);
    }

    [Theory]
    [InlineData("", "Message")]
    [InlineData("SHARED", "")]
    [InlineData("SHARED\n", "Message")]
    [InlineData("SHARED", "Message\u2028next")]
    public void Descriptor_validation_runs_when_the_identity_is_reserved(string id, string message)
    {
        var rules = new RuleSet();

        Assert.Throws<ArgumentException>(() => rules.Rule(id, message));
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
