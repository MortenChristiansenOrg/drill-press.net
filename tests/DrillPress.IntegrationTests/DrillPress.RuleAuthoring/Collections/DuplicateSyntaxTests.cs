using DrillPress.Collections;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Queries;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Collections;

public sealed class DuplicateSyntaxTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Token_shapes_ignore_trivia_but_preserve_literals_and_contexts()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { int M() => 1 + 2; int N() => 1 /*comment*/ + 2; int P() => 1 + 3; }"
                ),
            ]
        );
        workspace.AddProject("Other", [new("B.cs", "class B { int M() => 1 + 2; }")]);
        var query = DuplicateSyntax.In(Sources.Nodes<BinaryExpressionSyntax>(), minimumTokens: 3);

        var duplicates = query.In(workspace.Analyze(TestContext.Current.CancellationToken));

        Assert.Equal(
            ["1 + 2", "1 /*comment*/ + 2"],
            duplicates.Select(node => node.Syntax.ToString())
        );
    }
}
