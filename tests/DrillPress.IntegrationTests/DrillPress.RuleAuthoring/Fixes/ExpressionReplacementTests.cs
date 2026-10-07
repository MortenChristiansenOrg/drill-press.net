using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class ExpressionReplacementTests(SdkFixture fixture) : IClassFixture<SdkFixture>
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
        var rules = new RuleCatalog();
        rules
            .Rule("VALUE", "Inline the known expression.")
            .For(
                Code.Nodes<InvocationExpressionSyntax>()
                    .Where(node => node.Syntax.ToString() == "Value(value)")
            )
            .Forbid(fix: node =>
                Fix.For(node.AsExpression())
                    .ReplaceWith(SyntaxFactory.ParseExpression(replacement))
                    .SafeWhen(_ => true)
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
        var rules = new RuleCatalog();
        rules
            .Rule("VALUE", "Inline the default value.")
            .For(
                Code.Nodes<InvocationExpressionSyntax>()
                    .Where(node => node.Syntax.ToString() == "Value()")
            )
            .Forbid(fix: node =>
                Fix.For(node.AsExpression()).ReplaceWithLiteral(1).SafeWhen(_ => true)
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
        var rules = new RuleCatalog();
        rules
            .Rule("VALUE", "Use a literal.")
            .For(
                Code.Nodes<InvocationExpressionSyntax>()
                    .Where(node => node.Syntax.ToString() == "Value()")
            )
            .Forbid(fix: node =>
                Fix.For(node.AsExpression()).ReplaceWithLiteral(1).SafeWhen(_ => true)
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
        var node = Code.Nodes<InvocationExpressionSyntax>()
            .In(solution)
            .Single(node => node.Syntax.ToString() == "Value()");
        var proposal = Fix.For(node.AsExpression()).ReplaceWithLiteral(1).SafeWhen(_ => true);

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
        var call = Code.Calls.In(solution).Single(call => call.Target.Name == "Compare");
        var left = call.Argument("left")!.Value!;
        var right = call.Argument("right")!.Value!;
        var proposal = Fix.For(call)
            .ReplaceWith("{0} == {1}", left, right)
            .MustPreserve(
                ExpressionBehavior.EvaluationOrder | ExpressionBehavior.NullReceiverBehavior
            )
            .SafeWhen(_ => true);

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
        var call = Code.Nodes<InvocationExpressionSyntax>().In(solution).Single().AsExpression();
        var proposal = Fix.For(call)
            .ReplaceWith("{0} + {0}", call)
            .MustPreserve(ExpressionBehavior.EvaluationCounts)
            .SafeWhen(_ => true);

        var safe = proposal!.IsSafeIn(project);

        Assert.False(safe);
    }

    private static RuleCatalog EmptyRules()
    {
        var rules = new RuleCatalog();
        var member = CodeType.Of<string>().Member(nameof(string.Empty));
        rules
            .Rule("EMPTY", "Use a literal.")
            .For(CodeType.Of<string>().Member(nameof(string.Empty)).References)
            .Forbid(fix: reference =>
                Fix.For(reference)
                    .ReplaceWithLiteral("")
                    .SafeWhen(change => change.Before.RefersTo(member))
            );
        return rules;
    }
}
