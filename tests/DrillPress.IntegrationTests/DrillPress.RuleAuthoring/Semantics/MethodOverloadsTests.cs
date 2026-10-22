using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Semantics;

public sealed class MethodOverloadsTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Parameter_additions_keep_the_callers_selected_matching_policy()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "A",
            [
                new(
                    "A.cs",
                    """
                    using System.Threading;
                    class A {
                        void Same(int id) {} void Same(int id, CancellationToken token) {}
                        void Renamed(int id) {} public void Renamed(int value, CancellationToken token) {}
                        void Ref(int id) {} void Ref(ref int id, CancellationToken token) {}
                        void M() { Same(1); Renamed(2); Ref(3); }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var matches = Code
            .Calls.In(solution)
            .Select(call =>
                (
                    call.Target.Name,
                    call.Target.AddingParameter(CodeType.Of<CancellationToken>()).Count(),
                    call.Target.AddingParameter(CodeType.Of<CancellationToken>(), matchNames: false)
                        .Count()
                )
            )
            .ToArray();

        Assert.Equal(new[] { ("Same", 1, 1), ("Renamed", 0, 1), ("Ref", 0, 1) }, matches);
    }
}
