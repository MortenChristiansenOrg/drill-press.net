using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class BlockWrappingTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public async Task Independent_branches_wrap_together_without_a_final_newline()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { void M(bool test) { if (test) Run(); else Run(); } void Run() {} }"
                ),
            ]
        );

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.Equal([true, true], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(
            "class A { void M(bool test) { if (test) {\n    Run();\n} else {\n    Run();\n} } void Run() {} }",
            result.FixedText("A.cs")
        );
    }

    [Fact]
    public async Task Trailing_line_comments_stay_with_the_original_statement_and_crlf_is_retained()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A\r\n{\r\n    void M(bool test)\r\n    {\r\n        if (test)\r\n            Run(); // retained\r\n    }\r\n    void Run() {}\r\n}"
                ),
            ]
        );

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(result.Findings).HasFix);
        Assert.Equal(
            "class A\r\n{\r\n    void M(bool test)\r\n    {\r\n        if (test)\r\n        {\r\n            Run(); // retained\r\n        }\r\n    }\r\n    void Run() {}\r\n}",
            result.FixedText("A.cs")
        );
    }

    [Theory]
    [InlineData("class A { void M(bool test) { if (test) /* header */ Run(); } void Run() {} }")]
    [InlineData(
        "class A { void M(bool test) { if (test) Run(); } void Run([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) {} }"
    )]
    public async Task Ambiguous_header_trivia_and_changed_caller_information_withhold_the_fix(
        string source
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", source)]);

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.False(Assert.Single(result.Findings).HasFix);
        Assert.Equal(source, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Wrapping_an_else_if_keeps_the_complete_chain_as_one_statement()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { void M(bool a, bool b) { if (a) {} else if (b) Run(); else Run(); } void Run() {} }"
                ),
            ]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("BLOCK", "Wrap the selected statement.")
            .For(
                Code.IfStatements.Select(statement => statement.Else)
                    .Where(branch => branch is { IsElseIf: true })
            )
            .Forbid(fix: branch => Fix.For(branch!).AddBraces().Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(result.Findings).HasFix);
        Assert.Equal(
            "class A { void M(bool a, bool b) { if (a) {} else {\n    if (b) Run(); else Run();\n} } void Run() {} }",
            result.FixedText("A.cs")
        );
    }

    [Fact]
    public async Task Wrapping_an_outer_embedded_if_retains_the_inner_dangling_else_owner()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { void M(bool a, bool b) { if (a) if (b) Run(); else Run(); } void Run() {} }"
                ),
            ]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("BLOCK", "Wrap the selected statement.")
            .For(
                Code.IfStatements.Select(statement => statement.Then)
                    .Where(branch => branch.Syntax is IfStatementSyntax)
            )
            .Forbid(fix: branch => Fix.For(branch).AddBraces().Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(result.Findings).HasFix);
        Assert.Equal(
            "class A { void M(bool a, bool b) { if (a) {\n    if (b) Run(); else Run();\n} } void Run() {} }",
            result.FixedText("A.cs")
        );
    }

    private static RuleCatalog Rules()
    {
        var rules = new RuleCatalog();
        rules
            .Rule("BLOCK", "Wrap the selected statement.")
            .For(
                Code.IfStatements.SelectMany(statement =>
                        statement.Else is { } other
                            ? new[] { statement.Then, other }
                            : [statement.Then]
                    )
                    .Where(branch => branch.Syntax is ExpressionStatementSyntax)
            )
            .Forbid(fix: branch => Fix.For(branch).AddBraces().Propose());
        return rules;
    }
}
