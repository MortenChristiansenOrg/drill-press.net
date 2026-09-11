using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Semantics;

public sealed class SymbolQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Theory]
    [InlineData("net9.0")]
    [InlineData("net10.0")]
    public void Shared_source_references_match_each_framework_but_not_unrelated_declarations(
        string framework
    )
    {
        var workspace = fixture.Workspace();
        var shared = new TestSource("Shared.cs", "class A { void Read() { } void M() => Read(); }");
        workspace.AddProject("Library", [shared], framework: "net9.0");
        workspace.AddProject("Library", [shared], framework: "net10.0");
        workspace.AddProject("Unrelated", [shared with { Path = "Other.cs" }]);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var target = solution
            .Methods.Single(method =>
                method.Name == "Read"
                && method.Source.Project.Name == "Library"
                && method.Source.Project.TargetFramework == framework
            )
            .Symbol!;

        var references = SymbolQueries.ReferencesTo(target).In(solution);

        Assert.Equal(
            [
                ("Library", "net9.0", "Shared.cs", "Read"),
                ("Library", "net10.0", "Shared.cs", "Read"),
            ],
            references.Select(reference =>
                (
                    reference.Source.Project.Name,
                    reference.Source.Project.TargetFramework,
                    reference.Source.Document.Path,
                    reference.Syntax.ToString()
                )
            )
        );
    }
}
