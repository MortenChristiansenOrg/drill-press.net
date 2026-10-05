using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Rules;

public sealed class RuleScopeTests
{
    [Fact]
    public void Forbid_registers_a_rule_and_preserves_the_fluent_scope()
    {
        var rules = new RuleSet();
        var scope = rules.For(Code.MemberReferences);
        var reference = RuleTestData.Reference<string>("Empty");

        var returnedScope = scope.Forbid("TEST001", "Test message.");
        var diagnostics = rules.Evaluate([reference]);

        Assert.Same(scope, returnedScope);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("TEST001", diagnostic.Descriptor.Id);
        Assert.Equal("Test message.", diagnostic.Descriptor.Message);
        Assert.Equal(reference.Location, diagnostic.Location);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(RuleFixComplexity.Trivial)]
    [InlineData(RuleFixComplexity.Local)]
    [InlineData(RuleFixComplexity.Complex)]
    [InlineData(RuleFixComplexity.Architectural)]
    public void Declarations_preserve_optional_complexity(RuleFixComplexity? complexity)
    {
        var rules = new RuleSet();
        rules.For(Code.MemberReferences).Forbid("A", "Forbid.", fixComplexity: complexity);
        rules
            .For(Code.MemberReferences)
            .Require("B", "Require.", _ => false, fixComplexity: complexity);
        rules
            .For(Code.MemberReferences)
            .Forbid(new RuleDescriptor("C", "Descriptor.") { FixComplexity = complexity });
        var reference = RuleTestData.Reference<string>("Empty");

        var diagnostics = rules.Evaluate([reference]);

        Assert.Equal(
            new[]
            {
                new RuleDescriptor("A", "Forbid.") { FixComplexity = complexity },
                new RuleDescriptor("B", "Require.") { FixComplexity = complexity },
                new RuleDescriptor("C", "Descriptor.") { FixComplexity = complexity },
            },
            diagnostics.Select(diagnostic => diagnostic.Descriptor)
        );
    }
}
