using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class EmptyStringFixTests(SemanticRuleFixture fixture)
    : IClassFixture<SemanticRuleFixture>
{
    [Theory]
    [InlineData("string.Empty", "\"\"")]
    [InlineData("global::System.String.Empty", "\"\"")]
    [InlineData("nameof(string.Empty)", null)]
    public async Task Diagnoses_bound_aliases_and_nameof_but_only_fixes_values(
        string expression,
        string? replacement
    )
    {
        var project = fixture.Project($"class C {{ string Value => {expression}; }}");

        var findings = await fixture.Describe(project);

        Assert.Equal(
            [("DP1004", 1, expression.Replace("nameof(", "").TrimEnd(')'), replacement)],
            findings
        );
    }

    [Fact]
    public async Task Expression_tree_representation_is_preserved()
    {
        var project = fixture.Project(
            "class C { System.Linq.Expressions.Expression<System.Func<string>> Value => () => string.Empty; }"
        );

        var findings = await fixture.Describe(project);

        Assert.Equal([("DP1004", 1, "string.Empty", (string?)null)], findings);
    }

    [Fact]
    public async Task Constant_rewrite_cannot_change_an_enclosing_overload()
    {
        var project = fixture.Project(
            """
            class C
            {
                static string Select(short value) => "short";
                static string Select(long value) => "long";
                string Value => Select(string.Empty == "" ? 1 : 2);
            }
            """
        );

        var findings = await fixture.Describe(project);

        Assert.Equal([("DP1004", 5, "string.Empty", (string?)null)], findings);
    }

    [Fact]
    public async Task Invalid_enclosing_binding_withholds_the_fix()
    {
        var project = fixture.Project(
            "class C { string Value => Missing(string.Empty); }",
            allowErrors: true
        );

        var findings = await fixture.Describe(project);

        Assert.Equal([("DP1004", 1, "string.Empty", (string?)null)], findings);
    }

    [Fact]
    public async Task Inactive_linked_context_without_a_finding_withholds_the_fix()
    {
        const string source = """
            class C
            {
            #if ACTIVE
                string Value => string.Empty;
            #endif
            }
            """;
        var active = fixture.Project(source, symbols: ["ACTIVE"]);
        var inactive = fixture.Project(source, framework: "net9.0");

        var response = await fixture.Evaluate(active, inactive);

        Assert.Equal([1, 0], response.Contexts.Select(context => context.Findings.Length));
        Assert.Null(response.Contexts[0].Findings[0].BatchId);
        Assert.Empty(response.Batches);
    }

    [Fact]
    public async Task Agreeing_linked_contexts_share_one_complete_batch()
    {
        var first = fixture.Project("class C { string Value => string.Empty; }");
        var second = fixture.Project(
            "class C { string Value => string.Empty; }",
            framework: "net9.0"
        );

        var response = await fixture.Evaluate(first, second);

        var batch = Assert.Single(response.Batches);
        Assert.Equal(
            [first.Snapshot.ContextId, second.Snapshot.ContextId],
            batch.Validations.Select(validation => validation.ContextId)
        );
        Assert.Equal([true, true], batch.Validations.Select(validation => validation.IsSafe));
        Assert.Equal(
            [batch.Id, batch.Id],
            response
                .Contexts.SelectMany(context => context.Findings)
                .Select(finding => finding.BatchId)
        );
        Assert.Equal("\"\"", Assert.Single(batch.Edits).Replacement);
    }

    [Fact]
    public async Task Surrounding_comments_stay_outside_the_exact_replacement_span()
    {
        var project = fixture.Project(
            "class C { string Value => /* before */ string.Empty /* after */; }"
        );

        var response = await fixture.Evaluate(project);

        var edit = Assert.Single(Assert.Single(response.Batches).Edits);
        Assert.Equal(
            "class C { string Value => /* before */ \"\" /* after */; }",
            project
                .Snapshot.Documents[0]
                .Text.Remove(edit.Start, edit.Length)
                .Insert(edit.Start, edit.Replacement)
        );
    }

    [Fact]
    public async Task Interior_comments_are_not_discarded_by_a_literal_fix()
    {
        var project = fixture.Project("class C { string Value => string /* retained */ .Empty; }");

        var findings = await fixture.Describe(project);

        Assert.Equal([("DP1004", 1, "string /* retained */ .Empty", (string?)null)], findings);
    }
}
