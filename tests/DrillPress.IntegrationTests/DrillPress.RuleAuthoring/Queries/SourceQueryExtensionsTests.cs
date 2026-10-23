using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
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
        var files = Code.Files.Where(file => file.Name.AsSpan().StartsWith("Selected"));
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var nodes = files.Nodes<LiteralExpressionSyntax>().In(solution);
        var calls = files.Calls().In(solution);
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

    [Fact]
    public void Syntax_roots_preserve_order_trivia_and_scoped_query_independence()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Application",
            [new("A.cs", "#nullable disable\nclass A { int x = 1; }")]
        );
        workspace.AddProject("Tests", [new("T.cs", "class T { int x = 2; }")], isTest: true);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var tests = Code.Nodes<LiteralExpressionSyntax>().InTestProjects().In(solution);
        var all = Code.Nodes<LiteralExpressionSyntax>().In(solution);
        var directives = Code
            .Files.Nodes<NullableDirectiveTriviaSyntax>()
            .InNonTestProjects()
            .In(solution);

        Assert.Equal(["2"], tests.Select(node => node.Syntax.ToString()).ToArray());
        Assert.Equal(["1", "2"], all.Select(node => node.Syntax.ToString()).ToArray());
        Assert.Equal(
            ["#nullable disable"],
            directives.Select(node => node.Syntax.ToString()).ToArray()
        );
    }
}
