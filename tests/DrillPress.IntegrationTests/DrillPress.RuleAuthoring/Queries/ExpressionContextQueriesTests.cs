using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class ExpressionContextQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void References_share_expression_facts_and_nameof_filter_keeps_other_contexts()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    """
                    using System;
                    using System.Linq.Expressions;
                    class A
                    {
                        string Name => nameof(string.Empty);
                        Expression<Func<string>> Tree = () => string.Empty;
                        string Value => string /* keep */ .Empty;
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var query = CodeType.Of<string>().Member(nameof(string.Empty)).References;

        var references = query.In(solution);
        var selected = query.OutsideNameOf().In(solution);
        var facts = references
            .Select(reference =>
                $"{reference.Facts.IsInsideNameOf}:{reference.Facts.IsInsideExpressionTree}:{reference.Facts.HasComments}"
            )
            .ToArray();

        Assert.Equal(["True:False:False", "False:True:False", "False:False:True"], facts);
        Assert.Equal([references[1], references[2]], selected);
        Assert.All(
            references,
            reference => Assert.Equal(reference.Location, reference.Expression.Location)
        );
    }
}
