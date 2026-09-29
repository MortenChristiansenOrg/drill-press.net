using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class ReportingQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public async Task Hidden_proposals_still_require_successful_combined_compilation()
    {
        var workspace = fixture.Workspace();
        const string text =
            "class A { void M(int n) { switch (n) { case 1: break; case 2: break; } } }";
        workspace.AddProject("Library", [new("A.cs", text)]);
        var rules = new RuleSet();
        rules
            .For(Code.Nodes<LiteralExpressionSyntax>())
            .ReportOncePer(_ => "method")
            .Forbid(
                "ONE",
                "Use three.",
                fix: node =>
                    SourceChanges.Propose(
                        [SourceChanges.Replace(node.Source, node.Syntax.Span, "3")],
                        _ => true
                    )
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([new TestFinding("ONE", "A.cs", 1, 45, "1", false)], result.Findings);
        Assert.Equal(text, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Report_once_evaluates_and_applies_all_fixes_even_when_first_has_none()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [new("A.cs", "class A { int First = 1; int Second = 2; int Third = 3; }")]
        );
        var rules = new RuleSet();
        var evaluated = new List<string>();
        rules
            .For(Code.Nodes<LiteralExpressionSyntax>())
            .ReportOncePer(_ => "owner")
            .Forbid(
                "ONE",
                "Use four.",
                fix: node =>
                {
                    evaluated.Add(node.Syntax.ToString());
                    return node.Syntax.ToString() == "1"
                        ? null
                        : SourceChanges.Propose(
                            [SourceChanges.Replace(node.Source, node.Syntax.Span, "4")],
                            _ => true
                        );
                }
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(["1", "2", "3"], evaluated);
        Assert.Equal([new TestFinding("ONE", "A.cs", 1, 23, "1", true)], result.Findings);
        Assert.Equal(
            "class A { int First = 1; int Second = 4; int Third = 4; }",
            result.FixedText("A.cs")
        );
    }

    [Fact]
    public async Task Report_once_preserves_conflict_detection_between_hidden_candidates()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [new("A.cs", "class A { int First = 1; int Second = 2; }")]
        );
        var rules = new RuleSet();
        rules
            .For(Code.Nodes<LiteralExpressionSyntax>())
            .ReportOncePer(_ => "owner")
            .Forbid(
                "ONE",
                "Conflicting edits.",
                fix: node =>
                    SourceChanges.Propose(
                        [SourceChanges.Replace(node.Source, new(22, 1), node.Syntax.ToString())],
                        _ => true
                    )
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([new TestFinding("ONE", "A.cs", 1, 23, "1", false)], result.Findings);
        Assert.Equal("class A { int First = 1; int Second = 2; }", result.FixedText("A.cs"));
    }

    [Fact]
    public void Reporting_at_synthetic_or_foreign_syntax_is_rejected()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "class A { int Value = 1; }")]);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var rules = new RuleSet();
        rules.For(Code.LocalVariables).Forbid("EMPTY", "No locals.");
        rules
            .For(Code.Types)
            .Forbid(
                "BAD",
                "Bad anchor.",
                at: _ => Microsoft.CodeAnalysis.CSharp.SyntaxFactory.IdentifierName("missing")
            );

        var error = Assert.Throws<ArgumentException>(() => rules.Evaluate(solution));

        Assert.Equal(
            "The selected syntax must belong to the candidate's original compilation. (Parameter 'part')",
            error.Message
        );
    }
}
