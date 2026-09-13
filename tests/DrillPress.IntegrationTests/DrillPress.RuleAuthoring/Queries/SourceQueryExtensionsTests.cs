using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Queries;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class SourceQueryExtensionsTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void File_owned_selections_keep_the_original_scope_and_skip_generated_sources()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new("Selected.cs", "class Selected { void Run() => System.Console.WriteLine(1); }"),
                new("Other.cs", "class Other { void Run() => System.Console.WriteLine(2); }"),
                new(
                    "Selected.g.cs",
                    "class Generated { void Run() => System.Console.WriteLine(3); }",
                    true
                ),
            ]
        );
        var files = Sources.Files.Where(file => file.Name.AsSpan().StartsWith("Selected"));
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var nodes = files.Nodes<LiteralExpressionSyntax>().In(solution);
        var calls = files.Invocations().In(solution);
        var declarations = files.Declarations().In(solution);

        Assert.Equal(["1"], nodes.Select(node => node.Syntax.ToString()));
        Assert.Equal(
            ["System.Console.WriteLine(1)"],
            calls.Select(call => call.Operation.Syntax.ToString())
        );
        Assert.Equal(
            ["Selected", "Run"],
            declarations.Select(declaration => declaration.Symbol.Name)
        );
    }
}
