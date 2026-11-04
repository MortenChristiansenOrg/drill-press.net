using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Testing;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class LocalInliningTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    // Bodies start on line 16, indented by eight spaces.
    private const string Header = """
        using System;
        using System.Collections.Generic;
        using System.Diagnostics;
        using System.Runtime.CompilerServices;
        struct Counter { public int Count; public void Increment() => Count++; }
        class A
        {
            readonly List<int> _items = new();
            List<int> _mutable = new();
            Counter _counter;
            string? _name; static readonly int Shared = 1;
            static int Compute() => 1;
            static void Log(int value, [CallerLineNumber] int line = 0) { }
            int M(bool flag, int a, int b)
            {

        """;

    [Fact]
    public async Task A_local_returned_by_the_next_statement_is_replaced_by_its_initializer()
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
                        static int Compute() => 1;

                        int M()
                        {
                            var value = Compute();
                            return value;
                        }
                    }
                    """
                ),
            ]
        );

        var result = await workspace.CheckAsync(
            Rules(inlining => inlining.Propose()),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            [new TestFinding("INLINE", "A.cs", 7, 9, "var value = Compute()", true)],
            result.Findings
        );
        Assert.Equal(
            """
            class A
            {
                static int Compute() => 1;

                int M()
                {
                    return Compute();
                }
            }
            """,
            result.FixedText("A.cs")
        );
    }

    [Theory]
    [InlineData(
        "var value = a + b;\n_items.Add(value);\nreturn 0;",
        "_items.Add(a + b);\nreturn 0;"
    )]
    [InlineData("var sum = a + b;\nreturn sum * 2;", "return (a + b) * 2;")]
    [InlineData("var value = Compute();\n\nreturn value;", "return Compute();")]
    [InlineData(
        "_items.Clear();\n\nvar value = Compute();\n\nreturn value;",
        "_items.Clear();\n\nreturn Compute();"
    )]
    [InlineData(
        "var value = _items.Count;\nreturn value switch { 0 => a, _ => b };",
        "return _items.Count switch { 0 => a, _ => b };"
    )]
    [InlineData(
        "var evens = _items.FindAll(item => item % 2 == 0);\nreturn evens.Count;",
        "return _items.FindAll(item => item % 2 == 0).Count;"
    )]
    [InlineData(
        "var value = a > b\n    ? a\n    : b;\nreturn value;",
        "return a > b\n    ? a\n    : b;"
    )]
    [InlineData(
        "var value = Compute();\nif (value > a)\n    return 1;\nreturn 0;",
        "if (Compute() > a)\n    return 1;\nreturn 0;"
    )]
    [InlineData(
        "var value = Compute();\nint first = a, second = value;\nreturn first + second;",
        "int first = a, second = Compute();\nreturn first + second;"
    )]
    [InlineData(
        "switch (a)\n{\n    case 1:\n        var value = Compute();\n        return value;\n}\nreturn 0;",
        "switch (a)\n{\n    case 1:\n        return Compute();\n}\nreturn 0;"
    )]
    [InlineData("var value = _name;\nreturn value?.Length ?? 0;", "return _name?.Length ?? 0;")]
    [InlineData(
        "var value = flag ? a : b;\nreturn $\"{value}\".Length;",
        "return $\"{(flag ? a : b)}\".Length;"
    )]
    [InlineData(
        "var counter = new Counter();\ncounter.Increment();\nreturn 0;",
        "new Counter().Increment();\nreturn 0;"
    )]
    public async Task Propose_inlines_reads_that_follow_only_pure_evaluation(
        string body,
        string expected
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", Source(body))]);

        var result = await workspace.CheckAsync(
            Rules(inlining => inlining.Propose()),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(Source(expected), result.FixedText("A.cs"));
    }

    [Theory]
    [InlineData(
        "object value = 1;\nreturn value.GetHashCode();",
        "16:9",
        "the local's type differs from its initializer's type"
    )]
    [InlineData(
        "Func<int> value = Compute;\nreturn value();",
        "16:9",
        "the initializer is target-typed by the declaration"
    )]
    [InlineData(
        "using var value = new System.IO.MemoryStream();\nreturn value.Capacity;",
        "16:15",
        "a using declaration disposes its local"
    )]
    [InlineData(
        "const int value = 1;\nreturn value;",
        "16:15",
        "the local is a constant, which names its value"
    )]
    [InlineData(
        "ref int value = ref a;\nreturn value;",
        "16:9",
        "the local is a ref or scoped local"
    )]
    [InlineData(
        "var value = Compute();\nreturn value + value;",
        "16:9",
        "the local is not read exactly once"
    )]
    [InlineData(
        "var value = Compute();\nvalue = 2;\nreturn 0;",
        "16:9",
        "the local is written after its declaration or passed by reference"
    )]
    [InlineData(
        "var value = Compute();\nreturn nameof(value).Length;",
        "16:9",
        "the read is inside nameof or an expression tree"
    )]
    [InlineData(
        "var value = Compute();\nFunc<int> read = () => value;\nreturn read();",
        "16:9",
        "the read is deferred inside `() => value` at 17:26"
    )]
    [InlineData(
        "var value = Compute();\nreturn flag && value > 0 ? 1 : 0;",
        "16:9",
        "the read may not be evaluated in `flag && value > 0` at 17:16"
    )]
    [InlineData(
        "var value = Compute();\nreturn flag ? value : 0;",
        "16:9",
        "the read may not be evaluated in `flag ? value : 0` at 17:16"
    )]
    [InlineData(
        "var value = Compute();\nDebug.Assert(value > 0);\nreturn 0;",
        "16:9",
        "the read is part of `Debug.Assert(value > 0)` at 17:9, which a [Conditional] attribute or a missing partial implementation may compile away"
    )]
    [InlineData(
        "var value = Compute();\nwhile (flag) { _items.Add(value); }\nreturn 0;",
        "16:9",
        "the read is not evaluated once by a later statement of the same block"
    )]
    [InlineData(
        "var value = Compute();\nwhile (a < value) { a++; }\nreturn a;",
        "16:9",
        "the read is not evaluated once by a later statement of the same block"
    )]
    [InlineData(
        "var value = a + b;\nreturn checked(value * 2);",
        "16:9",
        "the read is inside checked or unchecked, which would change the initializer's overflow checking"
    )]
    [InlineData(
        "var counter = _counter;\ncounter.Increment();\nreturn 0;",
        "16:9",
        "`counter.Increment()` at 17:9 would use the initializer's variable instead of a copy"
    )]
    [InlineData(
        "var value = Compute();\nLog(value);\nreturn 0;",
        "16:9",
        "`Log(value)` at 17:9 receives different compiler-supplied arguments, such as a caller line number"
    )]
    [InlineData(
        "// the first value\nvar value = Compute();\nreturn value;",
        "17:9",
        "comments or directives on the declaration would be lost"
    )]
    [InlineData(
        "var value = Compute(); // the first value\nreturn value;",
        "16:9",
        "comments or directives on the declaration would be lost"
    )]
    [InlineData(
        "var value = Compute();\n_mutable.Add(value);\nreturn 0;",
        "16:9",
        "the initializer would move past `_mutable` at 17:9; use SafeWhen"
    )]
    [InlineData(
        "var value = Compute();\n_items.Clear();\nreturn value;",
        "16:9",
        "the initializer would move past `_items.Clear();` at 17:9; use SafeWhen"
    )]
    [InlineData(
        "var value = Compute();\nreturn new List<int> { value }.Count;",
        "16:9",
        "the initializer would move past `new List<int> { value }` at 17:16; use SafeWhen"
    )]
    [InlineData(
        "ref int alias = ref a;\nvar value = ++alias;\nreturn a + value;",
        "17:9",
        "the initializer would move past `a` at 18:16; use SafeWhen"
    )]
    [InlineData(
        "var value = Compute();\nreturn Shared + value;",
        "16:9",
        "the initializer would move past `Shared` at 17:16; use SafeWhen"
    )]
    public async Task Propose_withholds_inlining_that_could_change_meaning(
        string body,
        string location,
        string reason
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", Source(body))]);

        var result = await workspace.CheckAsync(
            Rules(inlining => inlining.Propose()),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            $"INLINE Inline the local.\nA.cs\n  {location}\n    fix withheld: {reason}\n",
            result.ExplainedOutput
        );
    }

    [Fact]
    public async Task SafeWhen_sees_the_operands_the_initializer_moves_past()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [new("A.cs", Source("var value = Compute();\n_mutable.Add(value);\nreturn 0;"))]
        );
        var skipped = new List<string>();

        var result = await workspace.CheckAsync(
            Rules(inlining =>
                inlining.SafeWhen(change =>
                {
                    skipped.AddRange(
                        change.SkippedOperands.Select(operand => operand.Syntax.ToString())
                    );
                    return change.SkippedStatements.Count == 0;
                })
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(["_mutable"], skipped);
        Assert.Equal(Source("_mutable.Add(Compute());\nreturn 0;"), result.FixedText("A.cs"));
    }

    [Fact]
    public async Task SafeWhen_sees_the_statements_the_initializer_moves_past()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [new("A.cs", Source("var value = Compute();\n_items.Clear();\nreturn value;"))]
        );
        var skipped = new List<string>();

        var result = await workspace.CheckAsync(
            Rules(inlining =>
                inlining.SafeWhen(change =>
                {
                    skipped.AddRange(
                        change.SkippedStatements.Select(statement => statement.Syntax.ToString())
                    );
                    return change.Initializer.Syntax.ToString() == "Compute()"
                        && change.Read.Syntax.ToString() == "value";
                })
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(["_items.Clear();"], skipped);
        Assert.Equal(Source("_items.Clear();\nreturn Compute();"), result.FixedText("A.cs"));
    }

    [Fact]
    public async Task SafeWhen_keeps_the_other_gates()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [new("A.cs", Source("var name = _name;\n_name = \"x\";\nreturn name?.Length ?? 0;"))]
        );

        var result = await workspace.CheckAsync(
            Rules(inlining => inlining.SafeWhen(_ => true), "name"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            "INLINE Inline the local.\nA.cs\n  16:9\n    fix withheld: the read's nullable state would change\n",
            result.ExplainedOutput
        );
    }

    [Fact]
    public async Task Chained_inlines_conflict_and_are_both_withheld()
    {
        var workspace = fixture.Workspace();
        var source = Source("var first = Compute();\nvar second = first;\nreturn second;");
        workspace.AddProject("Library", [new("A.cs", source)]);

        var result = await workspace.CheckAsync(
            Rules(inlining => inlining.Propose(), "first", "second"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal([false, false], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(source, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task A_linked_file_is_validated_in_every_project()
    {
        var workspace = fixture.Workspace();
        const string source = """
            class A
            {
                static int Compute() => 1;

            #if ALTERNATE
                [System.Diagnostics.Conditional("TRACE")]
            #endif
                static void Use(int value) { }

                int M()
                {
                    var value = Compute();
                    return value;
                }

                void N()
                {
                    var value = Compute();
                    Use(value);
                }
            }
            """;
        workspace.AddProject("Library", [new("A.cs", source)]);
        workspace.AddProject("Other", [new("A.cs", source)], symbols: ["ALTERNATE"]);

        var result = await workspace.CheckAsync(
            Rules(inlining => inlining.Propose()),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            """
            INLINE Inline the local.
            A.cs
              +12:9
              18:9
                fix withheld: Other (net10.0): the read is part of `Use(value)` at 19:9, which a [Conditional] attribute or a missing partial implementation may compile away; the read is part of `Use(value)` at 19:9, which a [Conditional] attribute or a missing partial implementation may compile away

            """.ReplaceLineEndings("\n"),
            result.ExplainedOutput
        );
        Assert.Equal(
            """
            class A
            {
                static int Compute() => 1;

            #if ALTERNATE
                [System.Diagnostics.Conditional("TRACE")]
            #endif
                static void Use(int value) { }

                int M()
                {
                    return Compute();
                }

                void N()
                {
                    var value = Compute();
                    Use(value);
                }
            }
            """,
            result.FixedText("A.cs")
        );
    }

    [Fact]
    public async Task Inlined_text_rechecks_cleanly()
    {
        var first = fixture.Workspace();
        first.AddProject(
            "Library",
            [new("A.cs", Source("var value = a + b;\n_items.Add(value);\nreturn _items.Count;"))]
        );
        var fixedText = (
            await first.CheckAsync(
                Rules(inlining => inlining.Propose()),
                TestContext.Current.CancellationToken
            )
        ).FixedText("A.cs");
        var second = fixture.Workspace();
        second.AddProject("Library", [new("A.cs", fixedText)]);

        var result = await second.CheckAsync(
            Rules(inlining => inlining.Propose()),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("", result.Output);
        Assert.Equal(Source("_items.Add(a + b);\nreturn _items.Count;"), fixedText);
    }

    private static string Source(string body) =>
        Header
        + string.Join(
            "\n",
            body.Split('\n').Select(line => line.Length == 0 ? "" : "        " + line)
        )
        + "\n    }\n}\n";

    private static RuleCatalog Rules(
        Func<LocalInlining, FixProposal?> finish,
        params string[] names
    )
    {
        names = names.Length == 0 ? ["value", "sum", "evens", "counter"] : names;
        var rules = new RuleCatalog();
        rules
            .Rule("INLINE", "Inline the local.")
            .For(
                Code.LocalVariables.Where(declaration =>
                    declaration.Variables.Any(variable =>
                        names.Contains(variable.Syntax.Identifier.ValueText)
                    )
                )
            )
            .Forbid(fix: declaration => finish(Fix.For(declaration).Inline()));
        return rules;
    }
}
