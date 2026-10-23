using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class SymbolRenameTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public async Task A_parameter_name_in_another_method_does_not_collide()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { void Save(int count) {} void M(int total) => Save(count: total); }"
                ),
            ]
        );

        var result = await workspace.CheckAsync(
            Rules(["count"], "total"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("RENAME Rename the declaration.\nA.cs\n  +1:25\n", result.Output);
        Assert.Equal(
            "class A { void Save(int total) {} void M(int total) => Save(total: total); }",
            result.FixedText("A.cs")
        );
    }

    [Theory]
    [InlineData(
        "class A { int M() { var count = 1; return count + count; } }",
        "class A { int M() { var total = 1; return total + total; } }"
    )]
    [InlineData(
        "class A { int M() { return Count(2); int Count(int value) => value; } }",
        "class A { int M() { return total(2); int total(int value) => value; } }"
    )]
    [InlineData(
        "class A { private int count; int M() => count + this.count; }",
        "class A { private int total; int M() => total + this.total; }"
    )]
    [InlineData(
        "class A { private int Count => 1; int M() => Count; }",
        "class A { private int total => 1; int M() => total; }"
    )]
    [InlineData(
        "class A { private int Count() => 1; int M() => Count() + nameof(Count).Length; }",
        "class A { private int total() => 1; int M() => total() + nameof(total).Length; }"
    )]
    public async Task Private_names_are_renamed_with_every_reference(string source, string expected)
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", source)]);

        var result = await workspace.CheckAsync(
            Rules(["count", "Count"], "total"),
            TestContext.Current.CancellationToken
        );

        Assert.True(Assert.Single(result.Findings).HasFix);
        Assert.Equal(expected, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Parameters_are_renamed_in_named_arguments_nameof_and_documentation()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    """
                    class A
                    {
                        /// <param name="value">The <paramref name="value"/> to use.</param>
                        private int M(int value) => nameof(value).Length + value;

                        int N() => M(value: 2);
                    }
                    """
                ),
            ]
        );

        var result = await workspace.CheckAsync(
            Rules(["value"], "size"),
            TestContext.Current.CancellationToken
        );

        Assert.True(Assert.Single(result.Findings).HasFix);
        Assert.Equal(
            """
            class A
            {
                /// <param name="size">The <paramref name="size"/> to use.</param>
                private int M(int size) => nameof(size).Length + size;

                int N() => M(size: 2);
            }
            """,
            result.FixedText("A.cs")
        );
    }

    [Fact]
    public async Task Renaming_one_overload_keeps_calls_to_the_others()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { private int Count(int v) => v; private int Count(string v) => 0; int M() => Count(1) + Count(\"a\"); }"
                ),
            ]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("RENAME", "Rename the overload.")
            .For(
                Code.Methods.Named("Count")
                    .Where(method =>
                        method.Symbol
                            is IMethodSymbol
                            {
                                Parameters: [{ Type.SpecialType: SpecialType.System_Int32 }]
                            }
                    )
            )
            .Forbid(fix: method => Fix.For(method).Rename("Total").Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(result.Findings).HasFix);
        Assert.Equal(
            "class A { private int Total(int v) => v; private int Count(string v) => 0; int M() => Total(1) + Count(\"a\"); }",
            result.FixedText("A.cs")
        );
    }

    [Fact]
    public async Task Keyword_names_are_escaped()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [new("A.cs", "class A { int M() { var count = 1; return count; } }")]
        );

        var result = await workspace.CheckAsync(
            Rules(["count"], "event"),
            TestContext.Current.CancellationToken
        );

        Assert.True(Assert.Single(result.Findings).HasFix);
        Assert.Equal(
            "class A { int M() { var @event = 1; return @event; } }",
            result.FixedText("A.cs")
        );
    }

    [Theory]
    [InlineData(
        "class A { int total; int M() { var count = 1; return count + total; } }",
        "1:36",
        "`total` at 1:62 binds differently after the rename"
    )]
    [InlineData(
        "class A { string M() { var count = 1; return \"count\" + count; } }",
        "1:28",
        "the string \"count\" at 1:46 may refer to the symbol by name"
    )]
    [InlineData(
        "class A { string M() { var count = 1; return \"\"\"count\"\"\" + count; } }",
        "1:28",
        "the string \"count\" at 1:46 may refer to the symbol by name"
    )]
    [InlineData(
        "class A { int M() { var count = 1;\n#if NEVER\n    count++;\n#endif\n    return count; } }",
        "1:25",
        "inactive code at 3:5 names \"count\", which the rename can neither update nor check"
    )]
    [InlineData(
        "class B { protected int total<T>(object v) => 1; } class A : B { private int Count<T>(T v) => 2; int M() => total<int>(0); }",
        "1:78",
        "`total<int>` at 1:109 binds differently after the rename"
    )]
    [InlineData(
        "class A { public int Count() => 1; }",
        "1:22",
        "the name is visible outside its type, where reflection, serialization or unloaded code can depend on it; use SafeWhen"
    )]
    [InlineData(
        "using System.Linq; class A { int M() => new[] { 1 }.Select(count => count).Sum(); }",
        "1:60",
        "lambda parameter names can be bound by name, as in minimal API handlers"
    )]
    [InlineData(
        "abstract class A { private protected abstract int Count(); }",
        "1:51",
        "the member is virtual, abstract, overriding or extern"
    )]
    [InlineData(
        "class WhereAttribute([System.Runtime.CompilerServices.CallerMemberName] string member = \"\") : System.Attribute { } class A { [Where] private int Count() => 1; }",
        "1:146",
        "`Where` at 1:127 receives different compiler-supplied arguments, such as a caller line number"
    )]
    public async Task Renames_that_could_change_meaning_are_withheld(
        string source,
        string location,
        string reason
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", source)]);

        var result = await workspace.CheckAsync(
            Rules(["count", "Count"], "total"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            $"RENAME Rename the declaration.\nA.cs\n  {location}\n    fix withheld: {reason}\n",
            result.ExplainedOutput
        );
    }

    [Fact]
    public async Task Private_members_of_a_partial_type_are_checked_in_its_other_files()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new("A.cs", "partial class C : B { private int Load() => 2; }"),
                new("B.cs", "partial class C { int M() => Fetch(); }"),
                new("C.cs", "class B { protected int Fetch() => 1; }"),
            ]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("RENAME", "Rename the method.")
            .For(Code.Methods.Named("Load"))
            .Forbid(fix: method => Fix.For(method).Rename("Fetch").Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            RENAME Rename the method.
            A.cs
              1:35
                fix withheld: `Fetch` at 1:30 binds differently after the rename

            """.ReplaceLineEndings("\n"),
            result.ExplainedOutput
        );
    }

    [Fact]
    public async Task Visible_members_are_renamed_across_their_project_with_a_proof()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new("A.cs", "public class A { public static int Count() => 1; }"),
                new("B.cs", "class B { int M() => A.Count() + A.Count(); }"),
            ]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("RENAME", "Rename the method.")
            .For(Code.Methods.Named("Count"))
            .Forbid(fix: method =>
                Fix.For(method).Rename("Total").SafeWhen(change => change.References == 2)
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(result.Findings).HasFix);
        Assert.Equal(
            "public class A { public static int Total() => 1; }",
            result.FixedText("A.cs")
        );
        Assert.Equal("class B { int M() => A.Total() + A.Total(); }", result.FixedText("B.cs"));
    }

    [Fact]
    public async Task Members_referenced_from_another_project_are_not_renamed()
    {
        var workspace = fixture.Workspace();
        var library = workspace.AddProject(
            "Library",
            [new("A.cs", "public class A { public static int Count() => 1; }")]
        );
        workspace.AddProject(
            "App",
            [new("B.cs", "class B { int M() => A.Count(); }")],
            dependencies: [library]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("RENAME", "Rename the method.")
            .For(Code.Methods.Named("Count"))
            .Forbid(fix: method => Fix.For(method).Rename("Total").SafeWhen(_ => true));

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            RENAME Rename the method.
            A.cs
              1:36
                fix withheld: B.cs in App references the symbol from another project, and project references are validated as they were

            """.ReplaceLineEndings("\n"),
            result.ExplainedOutput
        );
    }

    private static RuleCatalog Rules(string[] names, string newName)
    {
        var rules = new RuleCatalog();
        rules
            .Rule("RENAME", "Rename the declaration.")
            .For(Code.Declarations.Named(names))
            .Forbid(fix: declaration => Fix.For(declaration).Rename(newName).Propose());
        return rules;
    }
}
