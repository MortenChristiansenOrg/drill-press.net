using DrillPress.Facts;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Facts;

public sealed class ProjectFactTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Fact_values_are_lazy_and_separate_for_each_evaluated_context()
    {
        var workspace = fixture.Workspace();
        var first = workspace.AddProject("Library", [new("A.cs", "class A { }")], framework: "net9.0");
        var second = workspace.AddProject("Library", [new("A.cs", "class A { }")], framework: "net10.0");
        var computations = 0;
        var fact = new ProjectFact<string>(project => project.TargetFramework + ":" + ++computations);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var values = new[] { fact.In(solution, second), fact.In(solution, second), fact.In(solution, first) };

        Assert.Equal(["net10.0:1", "net10.0:1", "net9.0:2"], values);
        Assert.Equal(2, computations);
    }
}
