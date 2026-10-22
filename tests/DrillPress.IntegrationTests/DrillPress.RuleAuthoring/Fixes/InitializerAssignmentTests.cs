using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class InitializerAssignmentTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public async Task An_initializer_assignment_is_added_without_overwriting_a_written_choice()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "A",
            [
                new(
                    "A.cs",
                    "class A { public bool Enabled { get; set; } A M() => new A(); A N() => new A { Enabled = true }; }"
                ),
            ]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("INIT", "Disable by default.")
            .For(Code.ObjectCreations.Of(CodeType.Named("A")))
            .Forbid(fix: creation =>
                Fix.For(creation)
                    .AddInitializer("Enabled", "false")
                    .SafeWhen(value => value.Is(false))
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([true, false], result.Findings.Select(finding => finding.HasFix).ToArray());
        Assert.Equal(
            "class A { public bool Enabled { get; set; } A M() => new A() { Enabled = false }; A N() => new A { Enabled = true }; }",
            result.FixedText("A.cs")
        );
    }
}
