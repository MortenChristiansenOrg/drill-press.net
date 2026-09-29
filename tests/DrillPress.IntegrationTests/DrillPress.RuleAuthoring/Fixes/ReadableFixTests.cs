using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class ReadableFixTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public async Task Semantic_proofs_bind_before_and_after_to_their_actual_compilations()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "class A { string Value => string.Empty; }")]);
        var rules = new RuleSet();
        var empty = CodeType.Of<string>().Member("Empty");
        var different = new List<bool>();
        rules
            .For(empty.References)
            .Forbid(
                "EMPTY",
                "Use literal.",
                fix: reference =>
                    Fix.For(reference)
                        .ReplaceWith(Code.Literal(""))
                        .SafeWhen(change =>
                        {
                            var pair = change.Expressions!;
                            different.Add(
                                pair.Before.Source.Project.Compilation
                                    != pair.After.Source.Project.Compilation
                            );
                            return pair.Before.RefersTo(empty)
                                && pair.After.IsConstant("")
                                && pair.After.TypeIs<string>()
                                && !pair.After.Source.Document.IsEditable;
                        })
            );
        var missing = new MemberReference(
            CodeType.Of<string>(),
            "Empty",
            new("Missing.cs", 0, 0, 1, 1)
        );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);
        var unavailable = Fix.For(missing).ReplaceWith(Code.Literal("")).SafeWhen(_ => true);

        Assert.Equal("class A { string Value => \"\"; }", result.FixedText("A.cs"));
        Assert.All(different, Assert.True);
        Assert.Null(unavailable);
    }

    [Theory]
    [InlineData("{0} + {1}", true, "class A { int M(int a, int b) => ((a) + (b)); }")]
    [InlineData("{0} + {0}", false, "class A { int M(int a, int b) => a + b; }")]
    [InlineData("{0}", false, "class A { int M(int a, int b) => a + b; }")]
    public async Task Templates_preserve_precedence_and_account_for_repeated_and_unused_inputs(
        string template,
        bool expectedFix,
        string expectedText
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "class A { int M(int a, int b) => a + b; }")]);
        var rules = new RuleSet();
        rules
            .For(Code.Nodes<BinaryExpressionSyntax>())
            .Forbid(
                "TEMPLATE",
                "Use mapped expression.",
                fix: node =>
                    Fix.For(node)
                        .ReplaceWith(
                            Code.Expression(
                                template,
                                Fix.Input(node.Source, node.Syntax.Left),
                                Fix.Input(node.Source, node.Syntax.Right)
                            )
                        )
                        .MustPreserve(Behavior.Bindings | Behavior.EvaluationCounts)
                        .SafeWhen(change => change.Expressions!.After.TypeIs<int>())
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([expectedFix], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(expectedText, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Observable_source_facts_withhold_rewrites()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    """
                    using System;
                    using System.Linq.Expressions;
                    class A { Expression<Func<string>> Tree = () => string.Empty; string Name => nameof(string.Empty); string Value => string /* keep */ .Empty; }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var rules = new RuleSet();
        var references = CodeType.Of<string>().Member("Empty").References;
        rules
            .For(references)
            .Forbid(
                "EMPTY",
                "Keep gated values.",
                fix: reference =>
                    Fix.For(reference).ReplaceWith(Code.Literal("")).SafeWhen(_ => true)
            );

        var facts = references
            .In(solution)
            .Select(reference => new CodeExpression(reference.Source!, reference.Syntax!).Facts)
            .Select(fact =>
                $"{fact.IsInsideExpressionTree}:{fact.IsInsideNameOf}:{fact.HasComments}"
            )
            .ToArray();
        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(["True:False:False", "False:True:False", "False:False:True"], facts);
        Assert.Equal([false, false, false], result.Findings.Select(finding => finding.HasFix));
    }

    [Theory]
    [InlineData(true, "class A { bool M(string a, string b) => ((a) != (b)); }")]
    [InlineData(false, "class A { bool M(string a, string b) => !(string.Equals(a, b)); }")]
    public async Task Equality_can_absorb_negation_but_still_requires_behavior_proof(
        bool proven,
        string expected
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [new("A.cs", "class A { bool M(string a, string b) => !(string.Equals(a, b)); }")]
        );
        var rules = new RuleSet();
        rules
            .For(Code.Calls.Calling(CodeType.Of<string>().Member("Equals")))
            .Forbid(
                "EQUAL",
                "Use equality.",
                fix: call =>
                    Fix.For(call)
                        .ReplaceWithEquality(
                            Fix.Input(call.ArgumentsFor("a").Single().Value!),
                            Fix.Input(call.ArgumentsFor("b").Single().Value!),
                            absorbNegation: true
                        )
                        .MustPreserve(Behavior.EvaluationCounts)
                        .SafeWhen(_ => proven)
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([proven], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(expected, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task No_argument_modifier_proof_is_restricted_to_redundant_top_level_internal()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "internal class A { internal class B {} }")]);
        var rules = new RuleSet();
        rules
            .For(Code.TypeDeclarations.WithExplicitModifier(Modifier.Internal))
            .Forbid(
                "INTERNAL",
                "Omit redundant accessibility.",
                fix: type => Fix.For(type).RemoveModifier(Modifier.Internal).Propose()
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([true, false], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal("class A { internal class B {} }", result.FixedText("A.cs"));
    }

    [Fact]
    public void Literals_preserve_numeric_enum_and_text_types_when_rebound()
    {
        var workspace = fixture.Workspace();
        object[] values =
        [
            (byte)1,
            (sbyte)-2,
            (short)3,
            (ushort)4,
            5U,
            -6L,
            7UL,
            -0.0F,
            double.PositiveInfinity,
            8.5M,
            '"',
            "a\n\"b",
            StringComparison.Ordinal,
        ];
        var source =
            "class C { "
            + string.Join(
                " ",
                values.Select((value, index) => $"object M{index}() => {Code.Literal(value)};")
            )
            + " }";
        workspace.AddProject("Library", [new("A.cs", source)]);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var actual = Code
            .Methods.In(solution)
            .Select(method =>
                new CodeExpression(
                    method.Source,
                    method.Syntax.ExpressionBody!.Expression
                ).Type!.ToDisplayString()
            )
            .ToArray();

        Assert.Equal(
            [
                "byte",
                "sbyte",
                "short",
                "ushort",
                "uint",
                "long",
                "ulong",
                "float",
                "double",
                "decimal",
                "char",
                "string",
                "System.StringComparison",
            ],
            actual
        );
    }
}
