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
    [InlineData("namespace Company.Import;\n")]
    [InlineData("namespace Company.Import.Feeds;\n")]
    [InlineData("namespace Company.Import {\n")]
    public async Task Branches_in_qualified_namespaces_are_wrapped(string header)
    {
        var footer = header.EndsWith("{\n") ? "\n}" : "";
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    header
                        + "class A { void M(bool test) { if (test) Run(); } void Run() {} }"
                        + footer
                ),
                new("B.cs", "namespace Company.Other;\nclass B { }"),
            ]
        );

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(result.Findings).HasFix);
        Assert.Equal(
            header
                + "class A { void M(bool test) { if (test) {\n    Run();\n} } void Run() {} }"
                + footer,
            result.FixedText("A.cs")
        );
    }

    [Theory]
    [InlineData("for (var i = 0; i < 2; i++)", "")]
    [InlineData("foreach (var item in items)", "")]
    [InlineData("foreach (var (key, value) in pairs)", "")]
    [InlineData("while (items.Length > 3)", "")]
    [InlineData("do", " while (items.Length > 3);")]
    [InlineData("using (var stream = new System.IO.MemoryStream())", "")]
    [InlineData("lock (items)", "")]
    public async Task Every_embedded_statement_kind_is_wrapped(string header, string footer)
    {
        var workspace = fixture.Workspace();
        var prefix =
            $"class A\n{{\n    void M(string[] items, (int, int)[] pairs)\n    {{\n        {header}\n";
        workspace.AddProject(
            "Library",
            [new("A.cs", prefix + $"            Run();{footer}\n    }}\n    void Run() {{}}\n}}")]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("BLOCK", "Wrap the embedded statement.")
            .For(Code.EmbeddedStatements.WithoutBraces())
            .Forbid(fix: statement => Fix.For(statement).AddBraces().Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(result.Findings).HasFix);
        Assert.Equal(
            prefix
                + $"        {{\n            Run();\n        }}{footer}\n    }}\n    void Run() {{}}\n}}",
            result.FixedText("A.cs")
        );
    }

    [Fact]
    public async Task Stacked_using_statements_are_not_missing_braces()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { void M() { using (new System.IO.MemoryStream()) using (new System.IO.MemoryStream()) { } } }"
                ),
            ]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("BLOCK", "Wrap the embedded statement.")
            .For(Code.EmbeddedStatements.WithoutBraces())
            .Forbid(fix: statement => Fix.For(statement).AddBraces().Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task Expression_replacements_elsewhere_in_the_batch_do_not_withhold_braces()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { void M(bool test) { if (test) Run(); Use(string.Empty); } void Run() {} void Use(string value) {} }"
                ),
            ]
        );
        var rules = Rules();
        var empty = CodeType.Of<string>().Member(nameof(string.Empty));
        rules
            .Rule("EMPTY", "Use an empty literal.")
            .For(empty.References)
            .Forbid(fix: reference =>
                Fix.For(reference).ReplaceWithLiteral("").SafeWhen(change => change.After.Is(""))
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([true, true], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(
            "class A { void M(bool test) { if (test) {\n    Run();\n} Use(\"\"); } void Run() {} void Use(string value) {} }",
            result.FixedText("A.cs")
        );
    }

    [Theory]
    [InlineData("var size = nameof(test).Length;")]
    [InlineData("var items = new int[] { 1 };")]
    [InlineData("var jagged = new int[2][];")]
    [InlineData("var open = typeof(System.Collections.Generic.Dictionary<,>);")]
    [InlineData("var pairs = new System.Collections.Generic.Dictionary<int, int> { [1] = 2 };")]
    public async Task Branches_are_wrapped_beside_nameof_omitted_sizes_and_indexer_initializers(
        string statement
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    $"class A\n{{\n    void M(bool test)\n    {{\n        if (test)\n            Run();\n        {statement}\n    }}\n    void Run() {{}}\n}}"
                ),
            ]
        );

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            BLOCK Wrap the selected statement.
            A.cs
              +6:13

            """.ReplaceLineEndings("\n"),
            result.ExplainedOutput
        );
        Assert.Equal(
            $"class A\n{{\n    void M(bool test)\n    {{\n        if (test)\n        {{\n            Run();\n        }}\n        {statement}\n    }}\n    void Run() {{}}\n}}",
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
    public async Task Explained_output_names_the_check_that_withheld_the_fix()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A\n{\n    void M(bool test)\n    {\n        if (test) /* why */ Run();\n        if (test) Log();\n    }\n    void Run() {}\n    void Log([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) {}\n}"
                ),
            ]
        );

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            BLOCK Wrap the selected statement.
            A.cs
              5:29
                fix withheld: a comment between the header and the statement has no unambiguous place
              6:19
                fix withheld: `Log()` at 6:19 receives different compiler-supplied arguments, such as a caller line number

            """.ReplaceLineEndings("\n"),
            result.ExplainedOutput
        );
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
