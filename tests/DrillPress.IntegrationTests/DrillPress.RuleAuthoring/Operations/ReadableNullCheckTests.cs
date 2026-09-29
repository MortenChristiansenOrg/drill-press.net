using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class ReadableNullCheckTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Declaration_and_incoming_flow_are_independent_and_forms_remain_visible()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Checks",
            [
                new(
                    "C.cs",
                    """
                    #nullable enable
                    class C {
                        void M(string annotated, string? nullable, int? number) {
                            annotated = nullable;
                            if (annotated == null) {}
                            nullable = "known";
                            if (nullable is null) {}
                            if (!number.HasValue) {}
                        }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var values = Code
            .NullChecks.In(solution)
            .Select(check =>
                $"{check.Form}:{check.HasNonNullableDeclaration}:{check.IsKnownNotNullBeforeCheck}"
            )
            .ToArray();

        Assert.Equal(
            ["Equality:True:False", "Pattern:False:True", "NullableHasValue:False:False"],
            values
        );
    }
}
