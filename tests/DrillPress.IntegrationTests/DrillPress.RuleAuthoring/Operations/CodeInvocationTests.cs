using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class CodeInvocationTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Expression_views_retain_occurrences_and_filter_expression_trees_only_when_requested()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Views",
            [
                new(
                    "Views.cs",
                    """
                    class C {
                        static int Value() => 1;
                        void M(C? other) {
                            System.Linq.Expressions.Expression<System.Func<int>> tree = () => Value();
                            System.Func<int> function = () => Value();
                            other?.M(null);
                        }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var calls = Code.Calls.To(CodeType.Named("C").Member("Value"));

        var all = calls.Expressions().In(solution);
        var outside = calls.OutsideExpressionTrees().Expressions().In(solution);
        var conditional = Code.Calls.ToMethodsNamed("M").In(solution).Single();
        var union = calls.Expressions().Union(calls.Expressions()).In(solution);

        Assert.Equal(
            [true, false],
            all.Select(expression => expression.Facts.IsInsideExpressionTree)
        );
        Assert.Equal(all[1].Location, Assert.Single(outside).Location);
        Assert.Equal(
            all.Select(expression => expression.Location),
            union.Select(expression => expression.Location)
        );
        Assert.Equal(".M(null)", conditional.Expression!.Syntax.ToString());
        Assert.Equal(
            conditional.Source.Locate(conditional.Operation.Syntax.Span),
            conditional.Expression.Location
        );
        Assert.Same(conditional.Source, conditional.Expression.AsInvocation()!.Source);
    }

    [Fact]
    public void Calls_are_located_at_their_written_method_name()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Names",
            [
                new(
                    "Names.cs",
                    """
                    using System;
                    using System.Linq;
                    class C {
                        static T Make<T>() => default!;
                        int[] M(int[] values, C? other, Func<int> next) {
                            other?.M(values, null, next);
                            Local();
                            void Local() {}
                            _ = next() + Make<int>();
                            return values
                                .OrderBy(value => value)
                                .ThenBy(value => -value)
                                .ToArray();
                        }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var locations = Code
            .Calls.In(solution)
            .OrderBy(call => call.Location.Start)
            .Select(call =>
                (
                    call.Source.Document.Text.Substring(call.Location.Start, call.Location.Length),
                    call.Location.Line,
                    call.Location.Column
                )
            )
            .ToArray();

        Assert.Equal(
            [
                ("M", 6, 16),
                ("Local", 7, 9),
                ("next", 9, 13),
                ("Make<int>", 9, 22),
                ("OrderBy", 11, 14),
                ("ThenBy", 12, 14),
                ("ToArray", 13, 14),
            ],
            locations
        );
    }
}
