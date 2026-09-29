using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class NameQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Theory]
    [InlineData("Acme.**", new[] { "Root", "Child", "Leaf" })]
    [InlineData("Acme", new[] { "Root" })]
    [InlineData("Acme.*", new[] { "Child" })]
    [InlineData("**", new[] { "Global", "Root", "Child", "Leaf", "Other" })]
    [InlineData("**.Services", new[] { "Child" })]
    public void Namespace_globs_match_complete_segments_and_trailing_recursive_roots(
        string pattern,
        string[] expected
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class Global {} namespace Acme { class Root {} } namespace Acme.Services { class Child {} } namespace Acme.Services.Data { class Leaf {} } namespace AcmeOther { class Other {} }"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var selected = Code
            .Types.In(solution)
            .Where(type => type.Symbol.IsInNamespace(pattern))
            .Select(type => type.Name)
            .ToArray();

        Assert.Equal(expected, selected);
    }
}
