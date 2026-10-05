using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class CodeExpressionTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Invocation_views_unwrap_parentheses_but_do_not_invent_calls_for_casts_or_conditional_envelopes()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Views",
            [
                new(
                    "Views.cs",
                    """
                    class C {
                        string Get() => "";
                        void M(C? other) { object value = (Get()); object cast = (object)Get(); var maybe = other?.Get(); }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var parentheses = Code.Nodes<ParenthesizedExpressionSyntax>().In(solution).Single();
        var cast = Code.Nodes<CastExpressionSyntax>().In(solution).Single();
        var conditional = Code.Nodes<ConditionalAccessExpressionSyntax>().In(solution).Single();

        var call = new CodeExpression(parentheses.Source, parentheses.Syntax).AsInvocation();
        var castCall = new CodeExpression(cast.Source, cast.Syntax).AsInvocation();
        var conditionalCall = new CodeExpression(
            conditional.Source,
            conditional.Syntax
        ).AsInvocation();

        Assert.Equal("Get()", call!.Expression!.Syntax.ToString());
        Assert.Same(parentheses.Source, call.Source);
        Assert.Equal(call.Location, call.Expression.Location);
        Assert.True(call.Expression.TypeIs(CodeType.Of<string>()));
        Assert.True(call.Expression.ConvertedTypeIs(CodeType.Of<object>()));
        Assert.Null(castCall);
        Assert.Null(conditionalCall);
    }

    [Fact]
    public void Invalid_and_non_call_expressions_have_no_invocation_view()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Views",
            [new("Views.cs", "class C { void M() { Missing(); int value = 1; } }")],
            allowErrors: true
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var invalid = Code.Nodes<InvocationExpressionSyntax>().In(solution).Single();
        var literal = Code.Nodes<LiteralExpressionSyntax>().In(solution).Single();

        var views = new[]
        {
            new CodeExpression(invalid.Source, invalid.Syntax).AsInvocation(),
            new CodeExpression(literal.Source, literal.Syntax).AsInvocation(),
        };

        Assert.Equal([null, null], views);
    }
}
