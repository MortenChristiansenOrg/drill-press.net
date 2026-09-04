using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring;

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
        Assert.Equal(reference.Location, diagnostic.Location);
    }
}
