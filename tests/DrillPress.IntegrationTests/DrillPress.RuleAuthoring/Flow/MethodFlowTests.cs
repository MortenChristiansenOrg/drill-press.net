using DrillPress.Flow;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Flow;

public sealed class MethodFlowTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Nullable_state_and_flow_graph_follow_the_compiler_guard()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "class A { string M(string? value) { if (value is null) return \"\"; return value.Trim(); } }")]);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var method = solution.Methods.Single();
        var flow = MethodFlow.For(solution, method);
        var receiver = method.Syntax.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Single().Expression;

        var state = flow.NullState(receiver);
        var reads = flow.Data!.ReadInside.Select(symbol => symbol.Name).ToArray();

        Assert.Equal(NullableFlowState.NotNull, state);
        Assert.Equal(["value"], reads);
        Assert.NotNull(flow.Graph);
        Assert.Same(flow, MethodFlow.For(solution, method));
    }

    [Fact]
    public void Abstract_methods_have_no_executable_flow()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "abstract class A { public abstract void M(); }")]);
        var method = workspace.Analyze(TestContext.Current.CancellationToken).Methods.Single();

        var flow = new MethodFlow(method);

        Assert.Null(flow.Graph);
        Assert.Null(flow.Data);
    }
}
