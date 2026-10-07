using DrillPress.Engine;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class ReportingQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public async Task Reporting_groups_connect_disjoint_contexts_transitively()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Reporter",
            [
                new("A1.cs", "class A1 {}"),
                new("A2.cs", "class A2 {}"),
                new("B1.cs", "class B1 {}"),
                new("B2.cs", "class B2 {}"),
            ]
        );
        workspace.AddProject("Left", [new("Left.cs", "class Left { int Value = 1; }")]);
        workspace.AddProject("Middle", [new("Middle.cs", "class Middle { int Value = 1; }")]);
        workspace.AddProject("Right", [new("Right.cs", "class Right { int Value = 1; }")]);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var targets = Code.Nodes<LiteralExpressionSyntax>()
            .In(solution)
            .ToDictionary(node => node.Source.Document.Path);
        var destinations = new Dictionary<string, string>
        {
            ["A1.cs"] = "Left.cs",
            ["A2.cs"] = "Middle.cs",
            ["B1.cs"] = "Middle.cs",
            ["B2.cs"] = "Right.cs",
        };
        var rules = new RuleCatalog();
        rules
            .Rule("GROUP", "Update shared values.")
            .For(Code.Files.InProject("Reporter"))
            .ReportOncePer(file => file.Name[0])
            .Forbid(fix: file =>
            {
                var target = targets[destinations[file.Name]];
                return SourceChanges.Propose(
                    [SourceChanges.Replace(target.Source, target.Syntax.Span, "2")],
                    _ => true
                );
            });
        var expected = targets
            .Values.OrderBy(node => node.Source.Document.FileIdentity)
            .Select(node => SourceChanges.Replace(node.Source, node.Syntax.Span, "2"))
            .ToArray();
        var snapshot = CompilationSnapshot.Create(
            solution.Projects.Select(project => project.Snapshot).ToArray()
        );

        var response = await new AnalysisEngine().EvaluateAsync(
            rules,
            snapshot.RequestId,
            solution
                .Projects.Select(project => new CompilationContext(
                    project.Snapshot,
                    project.Compilation
                ))
                .ToArray(),
            TestContext.Current.CancellationToken
        );

        var batch = Assert.Single(response.Batches);
        Assert.Equal(expected, batch.Edits);
        Assert.Equal(
            [batch.Id, batch.Id],
            response
                .Contexts.SelectMany(context => context.Findings)
                .Select(finding => finding.BatchId)
        );
    }

    [Fact]
    public async Task Hidden_proposals_still_require_successful_combined_compilation()
    {
        var workspace = fixture.Workspace();
        const string text =
            "class A { void M(int n) { switch (n) { case 1: break; case 2: break; } } }";
        workspace.AddProject("Library", [new("A.cs", text)]);
        var rules = new RuleCatalog();
        rules
            .Rule("ONE", "Use three.")
            .For(Code.Nodes<LiteralExpressionSyntax>())
            .ReportOncePer(_ => "method")
            .Forbid(fix: node =>
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
        var rules = new RuleCatalog();
        var evaluated = new List<string>();
        rules
            .Rule("ONE", "Use four.")
            .For(Code.Nodes<LiteralExpressionSyntax>())
            .ReportOncePer(_ => "owner")
            .Forbid(fix: node =>
            {
                evaluated.Add(node.Syntax.ToString());
                return node.Syntax.ToString() == "1"
                    ? null
                    : SourceChanges.Propose(
                        [SourceChanges.Replace(node.Source, node.Syntax.Span, "4")],
                        _ => true
                    );
            });

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
        var rules = new RuleCatalog();
        rules
            .Rule("ONE", "Conflicting edits.")
            .For(Code.Nodes<LiteralExpressionSyntax>())
            .ReportOncePer(_ => "owner")
            .Forbid(fix: node =>
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
        var rules = new RuleCatalog();
        rules.Rule("EMPTY", "No locals.").For(Code.LocalVariables).Forbid();
        rules
            .Rule("BAD", "Bad anchor.")
            .For(Code.Types)
            .ReportAt(_ => Microsoft.CodeAnalysis.CSharp.SyntaxFactory.IdentifierName("missing"))
            .Forbid();

        var error = Assert.Throws<ArgumentException>(() => rules.Evaluate(solution));

        Assert.Equal(
            "The selected syntax must belong to the candidate's original compilation. (Parameter 'syntax')",
            error.Message
        );
    }
}
