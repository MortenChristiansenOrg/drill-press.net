using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class FixBuilderTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Theory]
    [InlineData("bool", "bool value", "!value", "(!value)")]
    [InlineData("int", "int value", "-value", "(-value)")]
    [InlineData("long", "int value", "(long)value", "((long)value)")]
    [InlineData("bool", "object value", "value is string", "(value is string)")]
    public async Task Non_primary_replacements_keep_precedence_in_member_receivers(
        string type,
        string parameter,
        string replacement,
        string expected
    )
    {
        var workspace = fixture.Workspace();
        var declaration =
            $"class A {{ {type} Value({parameter}) => {replacement}; string M({parameter}) => ";
        workspace.AddProject("Library", [new("A.cs", declaration + "Value(value).ToString(); }")]);
        var rules = new RuleSet();
        rules
            .For(
                Sources
                    .Nodes<InvocationExpressionSyntax>()
                    .Where(node => node.Syntax.ToString() == "Value(value)")
            )
            .Forbid(
                "VALUE",
                "Inline the known expression.",
                fix: node =>
                    Fix.For(node)
                        .ReplaceWith(SyntaxFactory.ParseExpression(replacement))
                        .Propose(_ => ProofResult.Proven)
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([true], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(declaration + expected + ".ToString(); }", result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Independent_optional_argument_calls_can_be_replaced_in_one_batch()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { int Value(int value = 1) => value; int M() => Value() + Value(); }"
                ),
            ]
        );
        var rules = new RuleSet();
        rules
            .For(
                Sources
                    .Nodes<InvocationExpressionSyntax>()
                    .Where(node => node.Syntax.ToString() == "Value()")
            )
            .Forbid(
                "VALUE",
                "Inline the default value.",
                fix: node =>
                    Fix.For(node)
                        .ReplaceWith(SyntaxFactory.ParseExpression("1"))
                        .Propose(_ => ProofResult.Proven)
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([true, true], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(
            "class A { int Value(int value = 1) => value; int M() => 1 + 1; }",
            result.FixedText("A.cs")
        );
    }

    [Fact]
    public async Task Individually_valid_edits_that_together_change_overload_are_withheld()
    {
        var workspace = fixture.Workspace();
        var source =
            "class A { int Value() => 1; void Pick(long a, long b) {} void Pick(byte a, byte b) {} void M() => Pick(Value(), Value()); }";
        workspace.AddProject("Library", [new("A.cs", source)]);
        var rules = new RuleSet();
        rules
            .For(
                Sources
                    .Nodes<InvocationExpressionSyntax>()
                    .Where(node => node.Syntax.ToString() == "Value()")
            )
            .Forbid(
                "VALUE",
                "Use a literal.",
                fix: node =>
                    Fix.For(node)
                        .ReplaceWith(SyntaxFactory.ParseExpression("1"))
                        .Propose(_ => ProofResult.Proven)
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([false, false], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(source, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Independent_edits_are_validated_and_applied_together()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [new("A.cs", "class A { string M() => string.Empty; string N() => string.Empty; }")]
        );

        var result = await workspace.CheckAsync(
            EmptyRules(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal([true, true], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(
            "class A { string M() => \"\"; string N() => \"\"; }",
            result.FixedText("A.cs")
        );
    }

    [Fact]
    public async Task A_proved_expression_replacement_preserves_exterior_trivia()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [new("A.cs", "class A { string M() => /* keep */ string.Empty; }")]
        );
        var rules = EmptyRules();

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            [new TestFinding("EMPTY", "A.cs", 1, 36, "string.Empty", true)],
            result.Findings
        );
        Assert.Equal("class A { string M() => /* keep */ \"\"; }", result.FixedText("A.cs"));
    }

    [Theory]
    [InlineData("class A { string M() => nameof(string.Empty); }")]
    [InlineData(
        "class A { System.Linq.Expressions.Expression<System.Func<string>> M() => () => string.Empty; }"
    )]
    [InlineData("class A { string M() => string./* keep */Empty; }")]
    [InlineData(
        "class A { string M() => Capture(string.Empty); string Capture(string x, [System.Runtime.CompilerServices.CallerArgumentExpression(\"x\")] string text = \"\") => text; }"
    )]
    public async Task Observable_syntax_and_interior_comments_keep_findings_without_fixes(
        string source
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", source)]);
        var rules = EmptyRules();

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([false], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(source, result.FixedText("A.cs"));
    }

    [Fact]
    public void A_changed_enclosing_overload_cannot_be_overridden_by_the_consumer_proof()
    {
        var workspace = fixture.Workspace();
        var project = workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { void M() => Pick(Value()); int Value() => 1; void Pick(byte value) {} void Pick(long value) {} }"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var node = Sources
            .Nodes<InvocationExpressionSyntax>()
            .In(solution)
            .Single(node => node.Syntax.ToString() == "Value()");
        var proposal = Fix.For(node)
            .ReplaceWith(SyntaxFactory.ParseExpression("1"))
            .Propose(_ => ProofResult.Proven);

        var safe = proposal!.IsSafeIn(project);

        Assert.False(safe);
    }

    [Theory]
    [InlineData("Compare(left: NextA(), right: NextB())", true)]
    [InlineData("Compare(right: NextB(), left: NextA())", false)]
    public void Evaluation_comparison_uses_source_order_even_when_parameter_roles_match(
        string expression,
        bool expected
    )
    {
        var workspace = fixture.Workspace();
        var project = workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    $$"""
                    class A { string NextA() => "a"; string NextB() => "b"; static bool Compare(string left, string right) => left == right; bool M() => {{expression}}; }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var call = Sources
            .Nodes<InvocationExpressionSyntax>()
            .In(solution)
            .Single(node => node.Syntax.ToString().StartsWith("Compare("));
        var left = Fix.Input(
            call.Source,
            call.Syntax.ArgumentList.Arguments.Single(argument =>
                argument.NameColon!.Name.Identifier.ValueText == "left"
            ).Expression
        );
        var right = Fix.Input(
            call.Source,
            call.Syntax.ArgumentList.Arguments.Single(argument =>
                argument.NameColon!.Name.Identifier.ValueText == "right"
            ).Expression
        );
        var replacement = SyntaxFactory.BinaryExpression(
            SyntaxKind.EqualsExpression,
            left.Syntax,
            right.Syntax
        );
        var proposal = Fix.For(call)
            .ReplaceWith(replacement)
            .MapInputs(left, right)
            .Require(RewriteChecks.SameEvaluationSequence)
            .Require(RewriteChecks.SameReceiverNullBehavior)
            .Propose(_ => ProofResult.Proven);

        var safe = proposal!.IsSafeIn(project);

        Assert.Equal(expected, safe);
    }

    [Fact]
    public void Duplicating_a_tracked_operand_fails_its_evaluation_count_proof()
    {
        var workspace = fixture.Workspace();
        var project = workspace.AddProject(
            "Library",
            [new("A.cs", "class A { int Next() => 1; int M() => Next(); }")]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var call = Sources.Nodes<InvocationExpressionSyntax>().In(solution).Single();
        var input = Fix.Input(call);
        var proposal = Fix.For(call)
            .ReplaceWith(
                SyntaxFactory.BinaryExpression(SyntaxKind.AddExpression, input.Syntax, input.Syntax)
            )
            .MapInputs(input)
            .Require(RewriteChecks.SameEvaluationCounts)
            .Propose(_ => ProofResult.Proven);

        var safe = proposal!.IsSafeIn(project);

        Assert.False(safe);
    }

    private static RuleSet EmptyRules()
    {
        var rules = new RuleSet();
        var member = CodeType.Of<string>().Member(nameof(string.Empty));
        rules
            .For(Code.MemberReferences.Where(Members.Are<string>(nameof(string.Empty))))
            .Forbid(
                "EMPTY",
                "Use a literal.",
                fix: reference =>
                    Fix.For(reference.Source!, reference.Syntax!)
                        .ReplaceWith(
                            SyntaxFactory.LiteralExpression(
                                SyntaxKind.StringLiteralExpression,
                                SyntaxFactory.Literal("")
                            )
                        )
                        .Propose(change =>
                            change.BeforeModel.GetSymbolInfo(change.Before).Symbol is { } symbol
                            && member.Matches(symbol)
                                ? ProofResult.Proven
                                : ProofResult.Unknown
                        )
            );
        return rules;
    }
}
