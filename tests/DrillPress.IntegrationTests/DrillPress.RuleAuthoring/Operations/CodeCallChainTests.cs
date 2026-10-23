using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class CodeCallChainTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void A_chain_holds_the_calls_before_and_after_a_call_in_every_spelling()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Chains",
            [
                new(
                    "Chains.cs",
                    """
                    using System.Linq;
                    class Node {
                        public Node? Next() => null;
                        public Node Self() => this;
                        static int[] Static(int[] values) =>
                            Enumerable.ToArray(Enumerable.Where(values, value => value > 0));
                        int[] Fluent(int[] values) =>
                            values.Where(value => value > 0).OrderBy(value => value).ThenBy(value => -value).ToArray();
                        Node? Conditional(Node? node) => node?.Next()?.Self().Next();
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var calls = Code.Calls.In(solution);
        var thenBy = calls.Single(call => call.Target.Name == "ThenBy");
        var where = calls.Single(call =>
            call.Target.Name == "Where"
            && call.Operation.Syntax.ToString().StartsWith("Enumerable", StringComparison.Ordinal)
        );
        var self = calls.Single(call => call.Target.Name == "Self");

        var fluent = thenBy.Chain();
        var spelled = where.Chain();
        var conditional = self.Chain();

        Assert.Equal(["Where", "OrderBy", "ThenBy", "ToArray"], Names(fluent.Calls));
        Assert.Equal(["Where", "OrderBy"], Names(fluent.Before(thenBy)));
        Assert.Equal(["ToArray"], Names(fluent.After(thenBy)));
        Assert.Equal("values", fluent.Root!.Syntax.ToString());
        Assert.Null(fluent.Local);
        Assert.Contains(fluent.Calls, call => call.Equals(thenBy));
        Assert.Equal(["Where", "ToArray"], Names(spelled.Calls));
        Assert.Equal("values", spelled.Root!.Syntax.ToString());
        Assert.Equal(["Next", "Self", "Next"], Names(conditional.Calls));
        Assert.Equal("node", conditional.Root!.Syntax.ToString());
    }

    [Fact]
    public void Chains_continue_through_the_local_they_start_from()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Chains",
            [
                new(
                    "Chains.cs",
                    """
                    using System.Linq;
                    class Builder {
                        public Builder Where(int value) => this;
                        public Builder And() => this;
                        public int Build() => 0;
                    }
                    class C {
                        int[] Query(int[] values) {
                            var query = values.Where(value => value > 0);
                            var other = values.Select(value => value);
                            query = query.OrderBy(value => value);
                            _ = other.Count();
                            return query.ToArray();
                        }
                        int Mutate() {
                            var builder = new Builder();
                            builder.Where(1);
                            builder.And().Where(2);
                            return builder.Build();
                        }
                        int Direct(int[] values) => values.Count();
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var calls = Code.Calls.In(solution);

        var query = calls.Single(call => call.Target.Name == "ToArray").Chain();
        var builder = calls.Single(call => call.Target.Name == "Build").Chain();
        var direct = calls
            .Single(call => call.Operation.Syntax.ToString() == "values.Count()")
            .Chain();

        Assert.Equal("query", query.Local!.Name);
        Assert.Equal(
            [
                ["Where"],
                ["OrderBy"],
                ["ToArray"],
            ],
            query.ThroughLocal().Select(chain => Names(chain.Calls))
        );
        Assert.Equal(
            [
                ["Where"],
                ["And", "Where"],
                ["Build"],
            ],
            builder.ThroughLocal().Select(chain => Names(chain.Calls))
        );
        Assert.Equal(
            [
                ["Count"],
            ],
            direct.ThroughLocal().Select(chain => Names(chain.Calls))
        );
    }

    private static string[] Names(IEnumerable<CodeInvocation> calls) =>
        calls.Select(call => call.Target.Name).ToArray();
}
