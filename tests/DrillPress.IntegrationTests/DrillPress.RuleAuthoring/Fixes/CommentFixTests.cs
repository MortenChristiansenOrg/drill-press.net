using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class CommentFixTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task Standalone_comment_removal_preserves_adjacent_blank_lines(string newline)
    {
        var workspace = fixture.Workspace();
        var source = """
            class A
            {
                void M()
                {

                    // Arrange

                    int value = 1;
                }
            }
            """.ReplaceLineEndings(newline);
        var expected = """
            class A
            {
                void M()
                {


                    int value = 1;
                }
            }
            """.ReplaceLineEndings(newline);
        workspace.AddProject("Library", [new("A.cs", source)]);
        var rules = new RuleCatalog();
        rules
            .Rule("COMMENT", "Remove phase labels.")
            .For(Code.Methods.Body().Comments())
            .Forbid(fix: comment => Fix.For(comment).Remove().Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            [new TestFinding("COMMENT", "A.cs", 6, 9, "// Arrange", true)],
            result.Findings
        );
        Assert.Equal(expected, result.FixedText("A.cs"));
    }

    [Theory]
    [InlineData("class A { int/* label */Value = 1; }", "class A { int Value = 1; }")]
    [InlineData("class A { int Value = 1; } // label", "class A { int Value = 1; } ")]
    [InlineData("class A {}\n// label", "class A {}\n")]
    [InlineData("/// docs\nclass A {}", "class A {}")]
    [InlineData("/* first\nsecond */\nclass A {}", "class A {}")]
    public async Task Removal_handles_inline_multiline_documentation_and_final_lines(
        string source,
        string expected
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", source)]);
        var rules = new RuleCatalog();
        rules
            .Rule("COMMENT", "Remove comment.")
            .For(Code.Files.Comments())
            .Forbid(fix: comment => Fix.For(comment).Remove().Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([true], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(expected, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Comment_and_type_corrections_validate_as_one_batch()
    {
        var workspace = fixture.Workspace();
        const string source = """
            class A
            {
                void M()
                {
                    // Arrange
                    int value = 1;
                }
            }
            """;
        workspace.AddProject("Library", [new("A.cs", source)]);
        var rules = new RuleCatalog();
        rules
            .Rule("COMMENT", "Remove comment.")
            .For(Code.Files.Comments())
            .Forbid(fix: comment => Fix.For(comment).Remove().Propose());
        rules
            .Rule("VAR", "Use var.")
            .For(Code.LocalVariables.WhereVarPreservesType())
            .Forbid(fix: declaration => Fix.For(declaration).UseVar().Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([true, true], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(
            """
            class A
            {
                void M()
                {
                    var value = 1;
                }
            }
            """,
            result.FixedText("A.cs")
        );
    }

    [Fact]
    public async Task Removal_withholds_changed_caller_line_information()
    {
        var workspace = fixture.Workspace();
        const string source = """
            using System.Runtime.CompilerServices;
            class A
            {
                static void Log([CallerLineNumber] int line = 0) {}
                void M()
                {
                    // keep line numbers
                    Log();
                }
            }
            """;
        workspace.AddProject("Library", [new("A.cs", source)]);
        var rules = new RuleCatalog();
        rules
            .Rule("COMMENT", "Remove comment.")
            .For(Code.Files.Comments())
            .Forbid(fix: comment => Fix.For(comment).Remove().Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([false], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(source, result.FixedText("A.cs"));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    [InlineData("\u0085")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public async Task Line_preserving_removal_keeps_caller_line_information(string newline)
    {
        var workspace = fixture.Workspace();
        var source = """
            using System.Runtime.CompilerServices;
            class A
            {
                static void Log([CallerLineNumber] int line = 0) {}
                void M()
                {
                    // Arrange
                    Log();
                }
            }
            """.ReplaceLineEndings(newline);
        workspace.AddProject("Library", [new("A.cs", source)]);
        var rules = CommentRules(preserveLines: true);

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            COMMENT Remove comment.
            A.cs
              +7:9

            """.ReplaceLineEndings("\n"),
            result.Output
        );
        Assert.Equal(
            """
            using System.Runtime.CompilerServices;
            class A
            {
                static void Log([CallerLineNumber] int line = 0) {}
                void M()
                {

                    Log();
                }
            }
            """.ReplaceLineEndings(newline),
            result.FixedText("A.cs")
        );
    }

    [Theory]
    [InlineData("class A { int/* label */Value = 1; }", "class A { int Value = 1; }")]
    [InlineData("class A {}\r\n// label", "class A {}\r\n")]
    [InlineData("/* first\r\nsecond */\r\nclass A {}", "\r\n\r\nclass A {}")]
    [InlineData("/* first\u2028second */\u2028class A {}", "\u2028\u2028class A {}")]
    [InlineData("/// first\n/// second\nclass A {}", "\n\nclass A {}")]
    [InlineData("class A { int/* first\nsecond */Value = 1; }", "class A { int\nValue = 1; }")]
    public async Task Line_preserving_removal_keeps_multiline_and_token_boundaries(
        string source,
        string expected
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", source)]);

        var result = await workspace.CheckAsync(
            CommentRules(preserveLines: true),
            TestContext.Current.CancellationToken
        );

        Assert.Equal([true], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(expected, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Line_preserving_removal_withholds_changed_caller_argument_text_in_linked_context()
    {
        var workspace = fixture.Workspace();
        var source = new TestSource(
            "A.cs",
            """
            using System.Runtime.CompilerServices;
            class A
            {
                static void Log(int value
            #if OBSERVABLE
                    , [CallerArgumentExpression("value")] string expression = ""
            #endif
                ) {}
                void M() => Log(1 /* label */ + 2);
            }
            """
        );
        workspace.AddProject("Primary", [source]);
        workspace.AddProject("Linked", [source], symbols: ["OBSERVABLE"]);
        var rules = new RuleCatalog();
        rules
            .Rule("COMMENT", "Remove comment.")
            .For(Code.Files.InProject("Primary").Comments())
            .Forbid(fix: comment => Fix.For(comment).Remove(preserveLines: true).Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            COMMENT Remove comment.
            A.cs
              9:23

            """.ReplaceLineEndings("\n"),
            result.Output
        );
        Assert.Equal(source.Text, result.FixedText("A.cs"));
    }

    private static RuleCatalog CommentRules(bool preserveLines)
    {
        var rules = new RuleCatalog();
        rules
            .Rule("COMMENT", "Remove comment.")
            .For(Code.Files.Comments())
            .Forbid(fix: comment => Fix.For(comment).Remove(preserveLines).Propose());
        return rules;
    }
}
