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
    public async Task A_caller_line_number_on_an_attribute_that_moves_withholds_the_conversion()
    {
        var workspace = fixture.Workspace();
        const string source = """
            using System;
            using System.Runtime.CompilerServices;
            namespace N {
                sealed class LineAttribute([CallerLineNumber] int line = 0) : Attribute
                {
                    public int Line { get; } = line;
                }

                [Line]
                class A { }
            }

            """;
        workspace.AddProject("A", [new("A.cs", source)]);

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            NS Use a file-scoped namespace.
            A.cs
              3
                fix withheld: `Line` at 9:6 receives different compiler-supplied arguments, such as a caller line number

            """.ReplaceLineEndings("\n"),
            result.ExplainedOutput
        );
        Assert.Equal(source, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task A_multiline_UTF8_raw_string_is_not_reindented()
    {
        var workspace = fixture.Workspace();
        const string source =
            "namespace N\n{\n    class A\n    {\n        byte[] M() => \"\"\"\n    abc\n\"\"\"u8.ToArray();\n    }\n}\n";
        workspace.AddProject("A", [new("A.cs", source)]);

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.False(Assert.Single(result.Findings).HasFix);
        Assert.Equal(source, result.FixedText("A.cs"));
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
