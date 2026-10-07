using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class ExpressionFixTests(SdkFixture fixture) : IClassFixture<SdkFixture>
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
            .Rule("EMPTY", "Use literal.")
            .For(empty.References)
            .Forbid(fix: reference =>
                Fix.For(reference)
                    .ReplaceWithLiteral("")
                    .SafeWhen(change =>
                    {
                        different.Add(
                            change.Before.Source.Project.Compilation
                                != change.After.Source.Project.Compilation
                        );
                        return change.Before.RefersTo(empty)
                            && change.After.Is("")
                            && change.After.TypeIs<string>()
                            && !change.After.Source.Document.IsEditable;
                    })
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal("class A { string Value => \"\"; }", result.FixedText("A.cs"));
        Assert.Equal([true], different);
    }

    [Theory]
    [InlineData("{1} + {0}", true, "class A { int M(int a, int b) => b + a; }")]
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
            .Rule("TEMPLATE", "Use mapped expression.")
            .For(Code.Nodes<BinaryExpressionSyntax>())
            .Forbid(fix: node =>
                Fix.For(node.AsExpression())
                    .ReplaceWith(
                        template,
                        new CodeExpression(node.Source, node.Syntax.Left),
                        new CodeExpression(node.Source, node.Syntax.Right)
                    )
                    .MustPreserve(ExpressionBehavior.EvaluationCounts)
                    .SafeWhen(change => change.After.TypeIs<int>())
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
            .Rule("EMPTY", "Keep gated values.")
            .For(references)
            .Forbid(fix: reference =>
                Fix.For(reference).ReplaceWithLiteral("").SafeWhen(_ => true)
            );

        var facts = references
            .In(solution)
            .Select(reference => reference.Facts)
            .Select(fact =>
                $"{fact.IsInsideExpressionTree}:{fact.IsInsideNameOf}:{fact.HasComments}"
            )
            .ToArray();
        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(["True:False:False", "False:True:False", "False:False:True"], facts);
        Assert.Equal([false, false, false], result.Findings.Select(finding => finding.HasFix));
    }

    [Theory]
    [InlineData(true, "class A { bool M(string a, string b) => a != b; }")]
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
            .Rule("EQUAL", "Use equality.")
            .For(Code.Calls.To(CodeType.Of<string>().Member("Equals")))
            .Forbid(fix: call =>
                Fix.For(call)
                    .ReplaceWithEquality(
                        call.Argument("a")!.Value!,
                        call.Argument("b")!.Value!,
                        absorbNegation: true
                    )
                    .MustPreserve(
                        ExpressionBehavior.EvaluationCounts
                            | ExpressionBehavior.EvaluationOrder
                            | ExpressionBehavior.NullReceiverBehavior
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
        var body = new CodeExpression(method.Source, method.Syntax.ExpressionBody!.Expression);

        var proposal = Fix.For(body).ReplaceWith(template, body).SafeWhen(_ => true);

        Assert.Equal(expected, proposal!.Edits.Single().Replacement);
    }

    [Theory]
    [InlineData("F() * 2", "a + b", "(a + b) * 2")]
    [InlineData("2 * F()", "a + b", "2 * (a + b)")]
    [InlineData("F() + 2", "a * b", "a * b + 2")]
    [InlineData("G(F())", "a + b", "G(a + b)")]
    [InlineData("F().ToString()", "a + b", "(a + b).ToString()")]
    public void Replacements_are_parenthesized_only_when_the_destination_requires_it(
        string body,
        string replacement,
        string expected
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    $"class A {{ int a, b; int F() => 1; int G(int value) => value; object M() => {body}; }}"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var call = Code.Calls.In(solution).Single(call => call.Target.Name == "F");

        var proposal = Fix.For(call)
            .ReplaceWith(Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression(replacement))
            .SafeWhen(_ => true);
        var method = Code.Methods.Named("M").In(solution).Single();
        var text = method.Syntax.ExpressionBody!.Expression.ToString();
        var edit = proposal!.Edits.Single();
        var start = edit.Start - method.Syntax.ExpressionBody.Expression.SpanStart;

        Assert.Equal(expected, text[..start] + edit.Replacement + text[(start + edit.Length)..]);
    }

    [Theory]
    [InlineData("string.Equals(left, right)", "left == right", true)]
    [InlineData("!string.Equals(left, right)", "left != right", true)]
    [InlineData(
        "string.Equals(left, right, System.StringComparison.Ordinal)",
        "left == right",
        true
    )]
    [InlineData(
        "!string.Equals(left, right, System.StringComparison.Ordinal)",
        "left != right",
        true
    )]
    [InlineData(
        "string.Equals(left ?? \"missing\", right)",
        "(left ?? \"missing\") == right",
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
            .Rule("EQUAL", "Use equality.")
            .For(Code.Calls.To(CodeType.Of<string>().Member("Equals")))
            .Forbid(fix: call =>
                Fix.For(call)
                    .ReplaceWithEquality(
                        call.Argument("a")!.Value!,
                        call.Argument("b")!.Value!,
                        absorbNegation: true
                    )
                    .MustPreserve(
                        ExpressionBehavior.EvaluationCounts
                            | ExpressionBehavior.EvaluationOrder
                            | ExpressionBehavior.NullReceiverBehavior
                    )
                    .SafeWhen(change => change.Kept.All(operand => operand.Before.TypeIs<string>()))
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([expectedFix], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(prefix + replacement + "; }", result.FixedText("A.cs"));
    }

    [Theory]
    [InlineData("\"x\".Equals(right, System.StringComparison.Ordinal)", "\"x\" == right", true)]
    [InlineData("!\"x\".Equals(right)", "\"x\" != right", true)]
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
            .Rule("EQUAL", "Use equality.")
            .For(Code.Calls.To(CodeType.Of<string>().Member("Equals")))
            .Forbid(fix: call =>
                Fix.For(call)
                    .ReplaceWithEquality(
                        call.Receiver!,
                        call.Argument("value")!.Value!,
                        absorbNegation: true
                    )
                    .MustPreserve(
                        ExpressionBehavior.EvaluationCounts
                            | ExpressionBehavior.EvaluationOrder
                            | ExpressionBehavior.NullReceiverBehavior
                    )
                    .SafeWhen(change => change.Kept.All(operand => operand.Before.TypeIs<string>()))
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
            .Rule("NEGATE", "Negate value.")
            .For(Code.Nodes<PrefixUnaryExpressionSyntax>().Expressions())
            .Forbid(fix: negation =>
                Fix.For(negation)
                    .ReplaceWith("!{0}", negation)
                    .MustPreserve(ExpressionBehavior.EvaluationOrder)
                    .SafeWhen(_ => true)
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([true], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal("class A { bool M(bool value) => !!value; }", result.FixedText("A.cs"));
    }

    [Fact]
    public async Task No_argument_modifier_proof_requires_unchanged_declared_accessibility()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "internal class A { internal class B {} }")]);
        var rules = new RuleSet();
        rules
            .Rule("INTERNAL", "Omit redundant accessibility.")
            .For(Code.TypeDeclarations.WithExplicitModifier(Modifier.Internal))
            .Forbid(fix: type => Fix.For(type).RemoveModifier(Modifier.Internal).Propose());

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
        workspace.AddProject("Seed", [new("Seed.cs", "class Seed { object M() => 0; }")]);
        var seed = Code
            .Methods.In(workspace.Analyze(TestContext.Current.CancellationToken))
            .Single();
        var zero = new CodeExpression(seed.Source, seed.Syntax.ExpressionBody!.Expression);
        var literals = values.Select(value =>
            Fix.For(zero).ReplaceWithLiteral(value).SafeWhen(_ => true)!.Edits.Single().Replacement
        );
        var source =
            "class C { "
            + string.Join(
                " ",
                literals.Select((literal, index) => $"object M{index}() => {literal};")
            )
            + " }";
        workspace.AddProject("Library", [new("A.cs", source)]);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var actual = Code
            .Methods.InProject("Library")
            .In(solution)
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
