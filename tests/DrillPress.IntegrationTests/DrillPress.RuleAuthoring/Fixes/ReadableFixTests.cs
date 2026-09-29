using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
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
    [InlineData("{0} + {1}", true, "class A { int M(int a, int b) => (a + b); }")]
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
    [InlineData(true, "class A { bool M(string a, string b) => (a != b); }")]
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
                        .MustPreserve(
                            Behavior.EvaluationCounts
                                | Behavior.EvaluationSequence
                                | Behavior.NullReceiverBehavior
                        )
                        .SafeWhen(_ => proven)
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([proven], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(expected, result.FixedText("A.cs"));
    }

    [Theory]
    [InlineData("{0} == b", "a", "a == b")]
    [InlineData("{0} == b", "a + b", "a + b == b")]
    [InlineData("{0} * c", "a + b", "(a + b) * c")]
    [InlineData("c - {0}", "a - b", "c - (a - b)")]
    [InlineData("{0} - c", "a - b", "a - b - c")]
    [InlineData("c + {0}", "a + b", "c + (a + b)")]
    [InlineData("{0}.ToString()", "a + b", "(a + b).ToString()")]
    [InlineData("{0}.ToString()", "a", "a.ToString()")]
    [InlineData("-{0}", "-a", "-(-a)")]
    [InlineData("c / {0}", "a * b", "c / (a * b)")]
    [InlineData("{0}.ToString()", "(long)a", "((long)a).ToString()")]
    [InlineData("{0} + c", "a > 0 ? b : c", "(a > 0 ? b : c) + c")]
    [InlineData("{0}", "a + b", "a + b")]
    [InlineData("F({0})", "a + b", "F(a + b)")]
    public void Templates_remove_only_parentheses_that_preserve_the_parsed_expression(
        string template,
        string operand,
        string expected
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [new("A.cs", $"class A {{ object M(int a, int b, int c) => {operand}; }}")]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var method = Code.Methods.Named("M").In(solution).Single();
        var input = Fix.Input(method.Source, method.Syntax.ExpressionBody!.Expression);

        var actual = Code.Expression(template, input).Syntax.NormalizeWhitespace().ToFullString();

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("string.Equals(left, right)", "(left == right)", true)]
    [InlineData("!string.Equals(left, right)", "(left != right)", true)]
    [InlineData(
        "string.Equals(left, right, System.StringComparison.Ordinal)",
        "(left == right)",
        true
    )]
    [InlineData(
        "!string.Equals(left, right, System.StringComparison.Ordinal)",
        "(left != right)",
        true
    )]
    [InlineData(
        "string.Equals(left ?? \"missing\", right)",
        "((left ?? \"missing\") == right)",
        true
    )]
    [InlineData("string.Equals(b: R(), a: L())", "string.Equals(b: R(), a: L())", false)]
    [InlineData(
        "string.Equals(left, right, Comparison())",
        "string.Equals(left, right, Comparison())",
        false
    )]
    public async Task Equality_preserves_operand_order_while_ignoring_constant_reads(
        string expression,
        string replacement,
        bool expectedFix
    )
    {
        var workspace = fixture.Workspace();
        const string prefix =
            "class A { string L() => \"l\"; string R() => \"r\"; System.StringComparison Comparison() => System.StringComparison.Ordinal; bool M(string left, string right) => ";
        workspace.AddProject("Library", [new("A.cs", prefix + expression + "; }")]);
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
                        .MustPreserve(
                            Behavior.EvaluationCounts
                                | Behavior.EvaluationSequence
                                | Behavior.NullReceiverBehavior
                        )
                        .SafeWhen(change =>
                            change.RetainedExpressions.All(operand =>
                                operand.Before.TypeIs<string>()
                            )
                        )
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([expectedFix], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(prefix + replacement + "; }", result.FixedText("A.cs"));
    }

    [Theory]
    [InlineData("\"x\".Equals(right, System.StringComparison.Ordinal)", "(\"x\" == right)", true)]
    [InlineData("!\"x\".Equals(right)", "(\"x\" != right)", true)]
    [InlineData("!left.Equals(right)", "!left.Equals(right)", false)]
    [InlineData("left.Equals(right)", "left.Equals(right)", false)]
    public async Task Equality_checks_instance_receiver_null_behavior_through_negation(
        string expression,
        string replacement,
        bool expectedFix
    )
    {
        var workspace = fixture.Workspace();
        const string prefix = "class A { bool M(string left, string right) => ";
        workspace.AddProject("Library", [new("A.cs", prefix + expression + "; }")]);
        var rules = new RuleSet();
        rules
            .For(Code.Calls.Calling(CodeType.Of<string>().Member("Equals")))
            .Forbid(
                "EQUAL",
                "Use equality.",
                fix: call =>
                    Fix.For(call)
                        .ReplaceWithEquality(
                            Fix.Input(
                                call.Source,
                                (ExpressionSyntax)call.Operation.Instance!.Syntax
                            ),
                            Fix.Input(call.ArgumentsFor("value").Single().Value!),
                            absorbNegation: true
                        )
                        .MustPreserve(
                            Behavior.EvaluationCounts
                                | Behavior.EvaluationSequence
                                | Behavior.NullReceiverBehavior
                        )
                        .SafeWhen(change =>
                            change.RetainedExpressions.All(operand =>
                                operand.Before.TypeIs<string>()
                            )
                        )
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([expectedFix], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(prefix + replacement + "; }", result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Evaluation_sequence_recognizes_a_retained_negation_before_unwrapping_it()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "class A { bool M(bool value) => !value; }")]);
        var rules = new RuleSet();
        rules
            .For(Code.Nodes<PrefixUnaryExpressionSyntax>())
            .Forbid(
                "NEGATE",
                "Negate value.",
                fix: node =>
                    Fix.For(node)
                        .ReplaceWith(Code.Expression("!{0}", Fix.Input(node)))
                        .MustPreserve(Behavior.EvaluationSequence)
                        .SafeWhen(_ => true)
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([true], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal("class A { bool M(bool value) => (!!value); }", result.FixedText("A.cs"));
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
