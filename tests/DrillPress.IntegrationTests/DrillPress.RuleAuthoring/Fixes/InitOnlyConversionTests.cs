using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class InitOnlyConversionTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public async Task Init_conversion_requires_policy_and_preserves_restricted_setters()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "A",
            [
                new(
                    "A.cs",
                    "class Request { public int Value { get; set; } public int Restricted { get; private set; } }"
                ),
            ]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("INIT", "Use init.")
            .For(Code.Properties)
            .Forbid(fix: property =>
                Fix.For(property)
                    .UseInitOnly()
                    .SafeWhen(symbol => symbol.ContainingType.Name == "Request")
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([true, false], result.Findings.Select(finding => finding.HasFix).ToArray());
        Assert.Equal(
            "class Request { public int Value { get; init; } public int Restricted { get; private set; } }",
            result.FixedText("A.cs")
        );
    }
}
