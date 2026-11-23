using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class FileScopedNamespaceConversionTests(SdkFixture fixture)
    : IClassFixture<SdkFixture>
{
    [Fact]
    public async Task A_brace_on_the_namespace_line_is_converted()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("A", [new("A.cs", "namespace N {\n    class A { }\n}\n")]);

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(result.Findings).HasFix);
        Assert.Equal("namespace N;\n\nclass A { }\n", result.FixedText("A.cs"));
    }

    [Fact]
    public async Task A_caller_line_number_that_moves_withholds_the_conversion()
    {
        var workspace = fixture.Workspace();
        const string source = """
            using System.Runtime.CompilerServices;
            namespace N {
                class A {
                    static int Line([CallerLineNumber] int line = 0) => line;
                    int M() => Line();
                }
            }

            """;
        workspace.AddProject("A", [new("A.cs", source)]);

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            NS Use a file-scoped namespace.
            A.cs
              2
                fix withheld: `Line()` at 5:20 receives different compiler-supplied arguments, such as a caller line number

            """.ReplaceLineEndings("\n"),
            result.ExplainedOutput
        );
        Assert.Equal(source, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task A_caller_line_number_and_file_path_on_an_attribute_that_moves_do_not_withhold_the_conversion()
    {
        var workspace = fixture.Workspace();
        var source = """
            using System;
            using System.Runtime.CompilerServices;
            namespace N {
                sealed class LineAttribute(
                    [CallerFilePath] string path = "",
                    [CallerLineNumber] int line = 0
                ) : Attribute
                {
                    public string Location { get; } = path + ":" + line;
                }

                [Line]
                class A { }
            }

            """.ReplaceLineEndings("\n");
        workspace.AddProject("A", [new("A.cs", source)]);

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            NS Use a file-scoped namespace.
            A.cs
              +3

            """.ReplaceLineEndings("\n"),
            result.ExplainedOutput
        );
        Assert.Equal(
            """
            using System;
            using System.Runtime.CompilerServices;
            namespace N;

            sealed class LineAttribute(
                [CallerFilePath] string path = "",
                [CallerLineNumber] int line = 0
            ) : Attribute
            {
                public string Location { get; } = path + ":" + line;
            }

            [Line]
            class A { }

            """.ReplaceLineEndings("\n"),
            result.FixedText("A.cs")
        );
    }

    [Fact]
    public async Task A_multiline_UTF8_raw_string_whose_value_would_change_withholds_the_conversion()
    {
        var workspace = fixture.Workspace();
        const string source =
            "namespace N\n{\n    class A\n    {\n        byte[] M() => \"\"\"\n    abc\n\"\"\"u8.ToArray();\n    }\n}\n";
        workspace.AddProject("A", [new("A.cs", source)]);

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.Equal(
            """"
            NS Use a file-scoped namespace.
            A.cs
              1
                fix withheld: `""" abc """u8` at 5:23 changes its value with the indentation

            """".ReplaceLineEndings("\n"),
            result.ExplainedOutput
        );
        Assert.Equal(source, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task A_multiline_raw_string_indented_with_the_code_is_converted()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "A",
            [
                new(
                    "A.cs",
                    """"
                    namespace N
                    {
                        class A
                        {
                            string M() => $"""
                                {nameof(A)}
                                  indented
                                """ + """
                                    text
                                    """;
                        }
                    }

                    """".ReplaceLineEndings("\n")
                ),
            ]
        );

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(result.Findings).HasFix);
        Assert.Equal(
            """"
            namespace N;

            class A
            {
                string M() => $"""
                    {nameof(A)}
                      indented
                    """ + """
                        text
                        """;
            }

            """".ReplaceLineEndings("\n"),
            result.FixedText("A.cs")
        );
    }

    [Fact]
    public async Task A_verbatim_string_whose_later_lines_are_indented_withholds_the_conversion()
    {
        var workspace = fixture.Workspace();
        const string source = """
            namespace N
            {
                class A
                {
                    string M() => @"first
                        second";
                }
            }

            """;
        workspace.AddProject("A", [new("A.cs", source)]);

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            NS Use a file-scoped namespace.
            A.cs
              1
                fix withheld: `@"first second"` at 5:23 is multiline text whose value the indentation change would alter

            """.ReplaceLineEndings("\n"),
            result.ExplainedOutput
        );
        Assert.Equal(source, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task A_migration_with_directives_before_the_namespace_and_documentation_comments_is_converted()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "A",
            [
                new(
                    "Migration.cs",
                    """
                    using System;

                    #nullable disable

                    namespace App.Migrations
                    {
                        /// <inheritdoc />
                        public partial class InitialCreate : IDisposable
                        {
                            #region Disposal
                            /// <inheritdoc />
                            public void Dispose() => Name = null;
                            #endregion

                            string Name { get; set; }
                        }
                    }

                    """.ReplaceLineEndings("\n")
                ),
            ]
        );

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(result.Findings).HasFix);
        Assert.Equal(
            """
            using System;

            #nullable disable

            namespace App.Migrations;

            /// <inheritdoc />
            public partial class InitialCreate : IDisposable
            {
                #region Disposal
                /// <inheritdoc />
                public void Dispose() => Name = null;
                #endregion

                string Name { get; set; }
            }

            """.ReplaceLineEndings("\n"),
            result.FixedText("Migration.cs")
        );
    }

    [Fact]
    public async Task A_conditional_directive_inside_the_namespace_withholds_the_conversion()
    {
        var workspace = fixture.Workspace();
        const string source = """
            namespace N
            {
            #if DEBUG
                class A { }
            #endif
            }

            """;
        workspace.AddProject("A", [new("A.cs", source)]);

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            NS Use a file-scoped namespace.
            A.cs
              1
                fix withheld: the conversion does not support the directive `#if DEBUG` at 3:1

            """.ReplaceLineEndings("\n"),
            result.ExplainedOutput
        );
        Assert.Equal(source, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task An_inactive_branch_that_declares_a_namespace_withholds_the_conversion()
    {
        var workspace = fixture.Workspace();
        const string source = """
            #if EXTRA
            namespace Hidden { }
            #else
            using System
            #endif
            ;
            namespace N
            {
                class A { }
            }

            """;
        workspace.AddProject("A", [new("A.cs", source)]);

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            NS Use a file-scoped namespace.
            A.cs
              7
                fix withheld: the inactive text at 2:1 may declare another namespace or member

            """.ReplaceLineEndings("\n"),
            result.ExplainedOutput
        );
        Assert.Equal(source, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Conditional_usings_are_kept()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "A",
            [
                new(
                    "A.cs",
                    """
                    #if EXTRA
                    using System.Text;
                    #else
                    using System;
                    #endif
                    namespace N
                    {
                        class A { }
                    }

                    """.ReplaceLineEndings("\n")
                ),
            ]
        );

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            #if EXTRA
            using System.Text;
            #else
            using System;
            #endif
            namespace N;

            class A { }

            """.ReplaceLineEndings("\n"),
            result.FixedText("A.cs")
        );
    }

    private static RuleCatalog Rules()
    {
        var rules = new RuleCatalog();
        rules
            .Rule("NS", "Use a file-scoped namespace.")
            .For(Code.Nodes<NamespaceDeclarationSyntax>())
            .Forbid(fix: declaration => Fix.For(declaration).UseFileScopedNamespace().Propose());
        return rules;
    }
}
