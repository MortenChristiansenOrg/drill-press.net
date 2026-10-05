using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class CodeEnumerationTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Loop_model_separates_original_collection_type_advancement_and_body_including_deconstruction()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Loops",
            [
                new(
                    "Loops.cs",
                    """
                    using System.Collections.Generic;
                    using System.Threading.Tasks;
                    class C {
                        async Task M(IEnumerable<int> items, IAsyncEnumerable<int> asyncItems, IEnumerable<(int,int)> pairs, int[] array) {
                            foreach (var item in items) { }
                            await foreach (var item in asyncItems) { }
                            foreach (var (first, second) in pairs) { }
                            foreach (var item in array) { }
                        }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var loops = Code.Enumerations.In(solution);

        Assert.Equal(
            ["items", "asyncItems", "pairs", "array"],
            loops.Select(loop => loop.SourceExpression.Syntax.ToString())
        );
        Assert.Equal([false, true, false, false], loops.Select(loop => loop.IsAsync));
        Assert.All(loops, loop => Assert.True(loop.IsResolved));
        Assert.Equal(
            ["MoveNext", "MoveNextAsync", "MoveNext", null],
            loops.Select(loop => loop.MoveNextMethod?.Name)
        );
        Assert.Equal(
            ["GetEnumerator", "GetAsyncEnumerator", "GetEnumerator", null],
            loops.Select(loop => loop.GetEnumeratorMethod?.Name)
        );
        Assert.Equal(
            ["int", "int", "(int, int)", "int"],
            loops.Select(loop => loop.ElementType!.ToDisplayString())
        );
        Assert.Equal(
            [
                "System.Collections.Generic.IEnumerable<int>",
                "System.Collections.Generic.IAsyncEnumerable<int>",
                "System.Collections.Generic.IEnumerable<(int, int)>",
                "int[]",
            ],
            loops.Select(loop => loop.SourceExpression.Type!.ToDisplayString())
        );
        Assert.All(loops, loop => Assert.Equal(loop.SourceExpression.Location, loop.Location));
        Assert.Equal(
            ["{ }", "{ }", "{ }", "{ }"],
            loops.Select(loop => loop.Body.Syntax.ToString())
        );
    }
}
