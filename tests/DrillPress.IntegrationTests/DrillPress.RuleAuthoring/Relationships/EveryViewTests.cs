using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Relationships;

public sealed class EveryViewTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Theory]
    [InlineData("class A : I {}", "class B : I {}", 2)]
    [InlineData("class A : I {}", "class B : I {} class C : I {}", 0)]
    [InlineData("class A : I {}", "class B {}", 0)]
    public void Every_framework_membership_of_the_same_source_interface_must_satisfy_count(
        string first,
        string second,
        int expected
    )
    {
        var workspace = fixture.Workspace();
        var a = workspace.AddProject(
            "Contracts",
            [new("I.cs", "public interface I {}")],
            framework: "net10.0"
        );
        var b = workspace.AddProject(
            "Contracts",
            [new("I.cs", "public interface I {}")],
            framework: "net8.0"
        );
        workspace.AddProject(
            "Product",
            [new("A.cs", first)],
            framework: "net10.0",
            dependencies: [a]
        );
        workspace.AddProject(
            "Product",
            [new("A.cs", second)],
            framework: "net8.0",
            dependencies: [b]
        );
        workspace.AddProject(
            "Tests",
            [new("Fake.cs", "class Fake : I {}")],
            framework: "net10.0",
            isTest: true,
            dependencies: [a]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var results = Code
            .Interfaces.ImplementationViews()
            .IgnoringTestProjects()
            .ConcreteOnly()
            .WithExactlyOneImplementation()
            .In(solution);
        var empty = CodeQuery<ImplementationView>
            .Create(_ => [])
            .WithExactlyOneImplementation()
            .In(solution);

        Assert.Equal(expected, results.Count);
        Assert.Empty(empty);
    }
}
