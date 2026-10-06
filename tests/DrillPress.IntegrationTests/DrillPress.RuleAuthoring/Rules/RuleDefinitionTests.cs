using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Rules;

public sealed class RuleDefinitionTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public async Task Call_and_loop_clauses_preserve_empty_advancement_unknown_evidence_and_outcome_policies()
    {
        var workspace = fixture.Workspace();
        var project = workspace.AddProject(
            "Queries",
            [
                new(
                    "Queries.cs",
                    """
                    using System.Collections.Generic;
                    using System.Threading.Tasks;
                    class C {
                        static IEnumerable<int> Query() => [];
                        static async IAsyncEnumerable<int> AsyncQuery() { await Task.CompletedTask; yield break; }
                        void Sync() {
                            Query();
                            foreach (var item in Query()) { }
                        }
                        async Task Async() {
                            await foreach (var item in AsyncQuery()) { }
                        }
                    }
                    """
                ),
            ]
        );
        var loops = Code.Enumerations.In(workspace.Analyze(TestContext.Current.CancellationToken));
        workspace.WithCoverage(facts =>
            facts
                .ForCall(project, "Queries.cs", "Query()", 0)
                .NotExecuted()
                .ForCall(project, "Queries.cs", "Query()", 1)
                .Executed()
                .ForEnumeration(loops[0])
                .Started()
                .ForCall("Queries.cs", "AsyncQuery()")
                .Unknown(CoverageReason.UnsupportedExpressionMapping)
                .ForEnumeration(loops[1])
                .Unknown(CoverageReason.UnsupportedEnumerationMapping)
        );
        var rules = new RuleSet();
        var policy = rules.Rule("DATA002", "Exercise each data query.");
        policy
            .For(Code.Calls.ToMethodsNamed("Query", "AsyncQuery").Expressions())
            .Require(
                global::DrillPress
                    .Coverage.Executed.OnUncovered("Exercise this call.")
                    .OnUnknown("Inspect call mapping.")
                    .ReviewUnknownFor(CoverageReason.UnsupportedExpressionMapping)
            );
        policy
            .For(Code.Enumerations)
            .Require(
                global::DrillPress
                    .Coverage.EnumerationStarted.OnUncovered("Start this loop.")
                    .OnUnknown("Inspect loop mapping.")
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equivalent(
            new TestFinding[]
            {
                new("DATA002", "Queries.cs", 7, 9, "Query()", false)
                {
                    Evidence = "coverage: uncovered",
                    OutcomeRemediation = "Exercise this call.",
                    Coverage =
                    [
                        new(
                            ExecutionCoverage.Uncovered,
                            [],
                            "Queries",
                            "net10.0",
                            project.Snapshot.ContextId
                        )
                        {
                            ReviewReasons = [CoverageReason.UnsupportedExpressionMapping],
                        },
                    ],
                },
                new("DATA002", "Queries.cs", 11, 36, "AsyncQuery()", false)
                {
                    Evidence =
                        "coverage: unknown (unsupported-mapping); enumeration: unknown (unsupported-enumeration)",
                    OutcomeRemediation = "Inspect call mapping.; Inspect loop mapping.",
                    Coverage =
                    [
                        new(
                            ExecutionCoverage.Unknown,
                            [CoverageReason.UnsupportedExpressionMapping],
                            "Queries",
                            "net10.0",
                            project.Snapshot.ContextId
                        )
                        {
                            ReviewReasons = [CoverageReason.UnsupportedExpressionMapping],
                        },
                        new(
                            ExecutionCoverage.Unknown,
                            [CoverageReason.UnsupportedEnumerationMapping],
                            "Queries",
                            "net10.0",
                            project.Snapshot.ContextId
                        )
                        {
                            Metric = CoverageMetric.Enumeration,
                        },
                    ],
                },
            },
            result.Findings,
            strict: true
        );
    }

    [Fact]
    public async Task Reporting_groups_are_clause_local_and_retain_hidden_fixes()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("C.cs", "class C { int A = 1; int B = 2; }")]);
        var rules = new RuleSet();
        var policy = rules.Rule("SHARED", "Update the class.");
        policy
            .For(Code.Nodes<LiteralExpressionSyntax>())
            .ReportOncePer(_ => "owner")
            .Forbid(fix: node =>
                SourceChanges.Propose(
                    [SourceChanges.Replace(node.Source, node.Syntax.Span, "4")],
                    _ => true
                )
            );
        policy.For(Code.Types).ReportOncePer(_ => "owner").Forbid(at: type => type);

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            new TestFinding[]
            {
                new("SHARED", "C.cs", 1, 7, "C", false),
                new("SHARED", "C.cs", 1, 19, "1", true),
            },
            result.Findings
        );
        Assert.Equal("class C { int A = 4; int B = 4; }", result.FixedText("C.cs"));
    }

    [Theory]
    [InlineData("2", true, "class C { int A = 2; }")]
    [InlineData("3", false, "class C { int A = 1; }")]
    public async Task Same_occurrence_clauses_deduplicate_output_and_validate_all_proposals(
        string secondReplacement,
        bool hasFix,
        string expectedText
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("C.cs", "class C { int A = 1; }")]);
        var rules = new RuleSet();
        var policy = rules.Rule("SHARED", "Update the value.");
        policy
            .For(Code.Nodes<LiteralExpressionSyntax>())
            .Forbid(fix: node =>
                SourceChanges.Propose(
                    [SourceChanges.Replace(node.Source, node.Syntax.Span, "2")],
                    _ => true
                )
            );
        policy
            .For(Code.Nodes<LiteralExpressionSyntax>())
            .Require(
                _ => false,
                fix: node =>
                    SourceChanges.Propose(
                        [SourceChanges.Replace(node.Source, node.Syntax.Span, secondReplacement)],
                        _ => true
                    )
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([new TestFinding("SHARED", "C.cs", 1, 19, "1", hasFix)], result.Findings);
        Assert.Equal(expectedText, result.FixedText("C.cs"));
    }

    [Fact]
    public async Task Individually_valid_clause_fixes_are_withheld_when_their_union_does_not_compile()
    {
        var workspace = fixture.Workspace();
        const string text =
            "class C { void M(int n) { switch (n) { case 1: break; case 2: break; } } }";
        workspace.AddProject("Library", [new("C.cs", text)]);
        var rules = new RuleSet();
        var policy = rules.Rule("SHARED", "Update the cases.");
        policy
            .For(Code.Nodes<LiteralExpressionSyntax>().Where(node => node.Syntax.ToString() == "1"))
            .Forbid(fix: node =>
                SourceChanges.Propose(
                    [SourceChanges.Replace(node.Source, node.Syntax.Span, "3")],
                    _ => true
                )
            );
        policy
            .For(Code.Nodes<LiteralExpressionSyntax>().Where(node => node.Syntax.ToString() == "2"))
            .Forbid(fix: node =>
                SourceChanges.Propose(
                    [SourceChanges.Replace(node.Source, node.Syntax.Span, "3")],
                    _ => true
                )
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            new TestFinding[]
            {
                new("SHARED", "C.cs", 1, 45, "1", false),
                new("SHARED", "C.cs", 1, 60, "2", false),
            },
            result.Findings
        );
        Assert.Equal(text, result.FixedText("C.cs"));
    }
}
