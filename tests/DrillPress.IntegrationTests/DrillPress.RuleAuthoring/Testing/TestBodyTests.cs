using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Testing;

public sealed class TestBodyTests(SemanticRuleFixture fixture) : IClassFixture<SemanticRuleFixture>
{
    [Fact]
    public async Task Reports_third_physical_blank_and_first_assertion_before_the_final_blank()
    {
        var project = fixture.Project(
            """
            using F = Xunit.FactAttribute;
            using A = Xunit.Assert;
            class C
            {
                [F] public void Test()
                {
                    var value = 1;

                    A.Equal(1, value);

                    value++;

                    A.Equal(2, value);
                }
            }
            """
        );

        var findings = await fixture.Describe(project);

        Assert.Equal(
            [("DP1001", 12, "", (string?)null), ("DP1002", 9, "A.Equal(1, value)", null)],
            findings
        );
    }

    [Theory]
    [InlineData(
        "Xunit.Assert.Throws<System.Exception>((System.Action)(() => throw new System.Exception()));",
        ""
    )]
    [InlineData(
        "Xunit.Assert.Throws(typeof(System.Exception), (System.Action)(() => throw new System.Exception()));",
        ""
    )]
    [InlineData(
        "Xunit.Assert.ThrowsAsync<System.Exception>(() => System.Threading.Tasks.Task.FromException(new System.Exception()));",
        "DP1002"
    )]
    [InlineData("Xunit.Assert.True(true);", "DP1002")]
    [InlineData(
        "Xunit.Assert.Throws<System.Exception>((System.Action)(() => throw new System.Exception())); Xunit.Assert.True(true);",
        "DP1002"
    )]
    public async Task Sole_Throws_is_the_only_assertion_exception(string assertion, string expected)
    {
        var project = fixture.Project(
            $$"""
            class C
            {
                [Xunit.Fact] public void Test()
                {
                    {{assertion}}

                    _ = 1;
                }
            }
            """
        );

        var findings = await fixture.Describe(project);

        Assert.Equal(expected, string.Join(",", findings.Select(finding => finding.Rule)));
    }

    [Fact]
    public async Task Derived_attributes_qualify_but_unrelated_names_and_expression_bodies_do_not()
    {
        var project = fixture.Project(
            """
            class DerivedAttribute : Xunit.FactAttribute { }
            class FactAttribute : System.Attribute { }
            class C
            {
                [Derived] public void Included()
                {
                    Xunit.Assert.True(true);

                    _ = 1;
                }
                [Fact] public void Excluded()
                {
                    Xunit.Assert.True(true);

                    _ = 1;
                }
                [Xunit.Fact] public void Expression() => Xunit.Assert.True(true);
                [Xunit.Fact] public void NoBlank() { Xunit.Assert.True(true); _ = 1; }
            }
            """
        );

        var findings = await fixture.Describe(project);

        Assert.Equal([("DP1002", 7, "Xunit.Assert.True(true)", (string?)null)], findings);
    }

    [Fact]
    public async Task Multiline_tokens_comments_and_nested_bodies_supply_no_empty_lines_or_assertions()
    {
        var project = fixture.Project(
            """"
            class C
            {
                [Xunit.Fact] public void Test()
                {
                    var raw = """



                        text
                        """;
                    /*



                    */
                    void Local()
                    {
                        Xunit.Assert.True(true);



                    }
                    System.Action lambda = () =>
                    {
                        Xunit.Assert.True(true);



                    };
                    Xunit.Assert.NotNull(raw);
                }
            }
            """"
        );

        var findings = await fixture.Describe(project);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task Whitespace_only_lines_count_across_separate_runs()
    {
        var project = fixture.Project(
            "class C { [Xunit.Fact] public void Test() {\n \n _ = 1;\n\t\n _ = 2;\n  \n } }"
        );

        var findings = await fixture.Describe(project);

        Assert.Equal([("DP1001", 6, "  ", (string?)null)], findings);
    }
}
