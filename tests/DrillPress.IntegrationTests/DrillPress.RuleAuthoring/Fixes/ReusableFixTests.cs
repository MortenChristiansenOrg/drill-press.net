using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class ReusableFixTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public async Task Collection_conversion_retains_elements_and_withholds_a_changed_concrete_type()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    """
                    using System.Collections.Generic;
                    class A {
                        List<int> a = new List<int> { 1, 2 };
                        IEnumerable<int> b = new List<int>();
                        int[] c = new int[0];
                        List<int> d = new List<int>(10);
                    }
                    """
                ),
            ]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("COLLECTION", "Use a collection expression.")
            .For(Code.Nodes<ExpressionSyntax>().Expressions().WithCollectionExpressionTarget())
            .Forbid(fix: expression => Fix.For(expression).UseCollectionExpression().Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            COLLECTION Use a collection expression.
            A.cs
              +3:19
              4:26
              +5:15

            """.ReplaceLineEndings("\n"),
            result.Output
        );
        Assert.Equal(
            """
            using System.Collections.Generic;
            class A {
                List<int> a = [1, 2];
                IEnumerable<int> b = new List<int>();
                int[] c = [];
                List<int> d = new List<int>(10);
            }
            """,
            result.FixedText("A.cs")
        );
    }

    [Fact]
    public async Task A_file_namespace_conversion_preserves_types_and_indentation()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    """
                    namespace Sample
                    {
                        class A { }
                    }
                    """.ReplaceLineEndings("\n")
                ),
            ]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("NAMESPACE", "Use a file namespace.")
            .For(Code.Nodes<NamespaceDeclarationSyntax>())
            .Forbid(fix: declaration => Fix.For(declaration).UseFileScopedNamespace().Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal("NAMESPACE Use a file namespace.\nA.cs\n  +1\n", result.Output);
        Assert.Equal("namespace Sample;\n\nclass A { }\n", result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Typed_argument_addition_selects_the_unique_bound_overload()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [new("A.cs", "class A { bool M(string s) => s.StartsWith(\"/\"); }")]
        );
        var ordinal = CodeType.Of<StringComparison>().Member("Ordinal");
        var rules = new RuleCatalog();
        rules
            .Rule("COMPARE", "Specify the comparison.")
            .For(Code.Calls.ToMethodsNamed("StartsWith"))
            .Forbid(fix: call =>
                Fix.For(call)
                    .AddArgumentOfType(
                        CodeType.Of<StringComparison>(),
                        "global::System.StringComparison.Ordinal",
                        change => change.AddedValue.RefersTo(ordinal)
                    )
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal("COMPARE Specify the comparison.\nA.cs\n  +1:33\n", result.Output);
        Assert.Equal(
            "class A { bool M(string s) => s.StartsWith(\"/\", global::System.StringComparison.Ordinal); }",
            result.FixedText("A.cs")
        );
    }

    [Fact]
    public async Task A_typed_argument_is_added_to_a_generic_overload()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "A",
            [
                new(
                    "A.cs",
                    """
                    using System.Threading;
                    class A {
                        static T Run<T>(T value) => value;
                        static T Run<T>(T value, CancellationToken token) => value;
                        int M() => Run(1);
                    }
                    """
                ),
            ]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("TOKEN", "Pass a token.")
            .For(Code.Calls.ToMethodsNamed("Run"))
            .Forbid(fix: call =>
                Fix.For(call)
                    .AddArgumentOfType(
                        CodeType.Of<CancellationToken>(),
                        "CancellationToken.None",
                        _ => true
                    )
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal("TOKEN Pass a token.\nA.cs\n  +5:16\n", result.Output);
        Assert.Equal(
            """
            using System.Threading;
            class A {
                static T Run<T>(T value) => value;
                static T Run<T>(T value, CancellationToken token) => value;
                int M() => Run(1, CancellationToken.None);
            }
            """,
            result.FixedText("A.cs")
        );
    }
}
