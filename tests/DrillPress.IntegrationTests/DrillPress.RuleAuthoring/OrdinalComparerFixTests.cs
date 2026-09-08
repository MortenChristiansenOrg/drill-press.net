using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring;

public sealed class OrdinalComparerFixTests(SemanticRuleFixture fixture) : IClassFixture<SemanticRuleFixture>
{
    [Theory]
    [InlineData("values.Distinct(System.StringComparer.Ordinal)", "()")]
    [InlineData("values.Distinct(comparer: System.StringComparer.Ordinal)", "()")]
    [InlineData("Enumerable.Distinct(values, System.StringComparer.Ordinal)", "(values )")]
    [InlineData("Enumerable.Distinct(comparer: System.StringComparer.Ordinal, source: values)", "( source: values)")]
    public async Task Exact_allowlist_validates_static_extension_and_named_mapping(string call, string replacement)
    {
        var project = fixture.Project($"using System.Linq; class C {{ object M(string[] values) => {call}; }}");

        var findings = await fixture.Describe(project);

        Assert.Equal([("DP1005", 1, "System.StringComparer.Ordinal", (string?)replacement)], findings);
    }

    [Theory]
    [InlineData("new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal)")]
    [InlineData("values.OrderBy(x => x, System.StringComparer.Ordinal)")]
    [InlineData("Custom(System.StringComparer.Ordinal)")]
    [InlineData("Optional(comparer: System.StringComparer.Ordinal)")]
    [InlineData("Params(System.StringComparer.Ordinal)")]
    public async Task Unknown_optional_params_constructor_and_ordering_apis_are_not_fixed(string call)
    {
        var project = fixture.Project($$"""
            using System;
            using System.Linq;
            class C
            {
                object M(string[] values) => {{call}};
                static object Custom(StringComparer comparer) => comparer;
                static object Optional(int value = 0, StringComparer? comparer = null) => value;
                static object Params(params StringComparer[] comparers) => comparers;
            }
            """);

        var findings = await fixture.Describe(project);

        Assert.Equal([("DP1005", 5, "System.StringComparer.Ordinal", (string?)null)], findings);
    }

    [Fact]
    public async Task Expression_tree_call_is_not_rewritten()
    {
        var project = fixture.Project("using System.Linq; class C { System.Linq.Expressions.Expression<System.Func<string[], object>> M() => values => values.Distinct(System.StringComparer.Ordinal); }");

        var findings = await fixture.Describe(project);

        Assert.Equal([("DP1005", 1, "System.StringComparer.Ordinal", (string?)null)], findings);
    }
    [Fact]
    public async Task Comparer_used_inside_a_nested_body_is_not_an_argument_reference()
    {
        var project = fixture.Project("class C { object M() => Run(() => { var comparer = System.StringComparer.Ordinal; return comparer; }); object Run(System.Func<object> action) => action(); }");

        var findings = await fixture.Describe(project);

        Assert.Empty(findings);
    }
}
