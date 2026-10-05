using DrillPress.Testing;
using Xunit;

namespace DrillPress.UnitTests.Testing;

public sealed class RuleTestWorkspaceTests
{
    private const string Prelude =
        "namespace System { public class Object {} public class ValueType {} public struct Void {} }";
    private const string Source =
        Prelude + "\nclass C { static void Hit() {} void M() { Hit(); Hit(); } }";

    [Fact]
    public async Task Synthetic_call_states_flow_through_the_normal_validated_finding_path()
    {
        var workspace = new RuleTestWorkspace([]);
        var project = workspace.AddProject("Product", [new("Calls.cs", Source)]);
        workspace.WithCoverage(facts =>
            facts
                .ForCall(project, "Calls.cs", "Hit()", occurrenceIndex: 0)
                .Executed()
                .ForCall(project, "Calls.cs", "Hit()", occurrenceIndex: 1)
                .NotExecuted()
        );
        var rules = ExecutionRules();

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equivalent(
            new TestFinding("CALL", "Calls.cs", 2, 50, "Hit()", false)
            {
                Evidence = "coverage: uncovered",
                Coverage =
                [
                    new(
                        ExecutionCoverage.Uncovered,
                        [],
                        "Product",
                        "net10.0",
                        project.Snapshot.ContextId
                    ),
                ],
            },
            Assert.Single(result.Findings),
            strict: true
        );
    }

    [Fact]
    public async Task Explicit_and_missing_synthetic_evidence_remain_unknown()
    {
        var workspace = new RuleTestWorkspace([]);
        var project = workspace.AddProject("Product", [new("Calls.cs", Source)]);
        workspace.WithCoverage(facts =>
            facts
                .ForCall(project, "Calls.cs", "Hit()", 0)
                .Unknown(CoverageReason.UnsupportedExpressionMapping)
        );

        var result = await workspace.CheckAsync(
            ExecutionRules(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            ["coverage: unknown (unsupported-mapping)", "coverage: unknown (loose-source)"],
            result.Findings.Select(finding => finding.Evidence)
        );
        Assert.Equal(
            [ExecutionCoverage.Unknown, ExecutionCoverage.Unknown],
            result.Findings.Select(finding => Assert.Single(finding.Coverage).State)
        );
        Assert.Equal(
            [CoverageReason.UnsupportedExpressionMapping, CoverageReason.LooseSource],
            result.Findings.Select(finding =>
                Assert.Single(Assert.Single(finding.Coverage).Reasons)
            )
        );
    }

    [Fact]
    public void Text_only_selectors_reject_repeated_calls_and_linked_framework_documents()
    {
        var workspace = new RuleTestWorkspace([]);
        workspace.AddProject("Product", [new("Calls.cs", Source)], framework: "net9.0");
        workspace.AddProject("Product", [new("Calls.cs", Source)], framework: "net10.0");

        var call = Assert.Throws<ArgumentException>(() =>
            workspace.WithCoverage(facts => facts.ForCall("Calls.cs", "Hit()"))
        );
        var file = Assert.Throws<ArgumentException>(() =>
            workspace.WithCoverage(facts => facts.ForFile("Calls.cs"))
        );

        Assert.Contains("exactly one", call.Message);
        Assert.Contains("exactly one", file.Message);
    }

    [Fact]
    public async Task Linked_framework_memberships_do_not_share_synthetic_execution_facts()
    {
        var workspace = new RuleTestWorkspace([]);
        var nine = workspace.AddProject("Product", [new("Calls.cs", Source)], framework: "net9.0");
        var ten = workspace.AddProject("Product", [new("Calls.cs", Source)], framework: "net10.0");
        workspace.WithCoverage(facts =>
            facts
                .ForCall(nine, "Calls.cs", "Hit()", 0)
                .Executed()
                .ForCall(nine, "Calls.cs", "Hit()", 1)
                .Executed()
                .ForCall(ten, "Calls.cs", "Hit()", 0)
                .NotExecuted()
                .ForCall(ten, "Calls.cs", "Hit()", 1)
                .Unknown(CoverageReason.PartialRange)
        );

        var result = await workspace.CheckAsync(
            ExecutionRules(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            ["net10.0", "net10.0"],
            result.Findings.Select(finding => Assert.Single(finding.Coverage).Framework)
        );
        Assert.Equal(
            [ten.Snapshot.ContextId, ten.Snapshot.ContextId],
            result.Findings.Select(finding => Assert.Single(finding.Coverage).ContextId)
        );
        Assert.Equal(
            [ExecutionCoverage.Uncovered, ExecutionCoverage.Unknown],
            result.Findings.Select(finding => Assert.Single(finding.Coverage).State)
        );
    }

    [Fact]
    public async Task Line_counts_distinguish_actual_percentages_incomplete_data_and_zero_coverable_lines()
    {
        var workspace = new RuleTestWorkspace([]);
        workspace.AddProject(
            "Product",
            [
                new("Low.cs", Source),
                new("Incomplete.cs", Source.Replace(Prelude, "").Replace("class C", "class D")),
                new("Zero.cs", Source.Replace(Prelude, "").Replace("class C", "class E")),
            ]
        );
        workspace.WithCoverage(facts =>
            facts
                .ForFile("Low.cs")
                .Lines(1, 4)
                .ForFile("Incomplete.cs")
                .Lines(1, 4, false, CoverageReason.MissingSymbols)
                .ForFile("Zero.cs")
                .Lines(0, 0)
        );
        var rules = new RuleSet();
        rules
            .For(Code.Files)
            .Require(global::DrillPress.Coverage.Line.AtLeast(50), "LINES", "Exercise source.");

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["Incomplete.cs", "Low.cs", "Zero.cs"],
            result.Findings.Select(finding => finding.Path)
        );
        Assert.Equivalent(
            new[]
            {
                new LineCoverageMeasurement(
                    1,
                    4,
                    false,
                    [CoverageReason.MissingSymbols, CoverageReason.IncompleteLineEvidence]
                ),
                new LineCoverageMeasurement(1, 4, true, []),
                new LineCoverageMeasurement(0, 0, true, [CoverageReason.ZeroCoverableLines]),
            },
            result.Findings.Select(finding => Assert.Single(finding.Coverage).Lines).ToArray(),
            strict: true
        );
        Assert.Equal(
            [
                "line coverage: unknown (1/4; missing-symbols, incomplete-lines)",
                "line coverage: 25% (1/4), required 50%",
                "line coverage: unknown (0/0; zero-coverable-lines)",
            ],
            result.Findings.Select(finding => finding.Evidence)
        );
    }

    [Fact]
    public async Task Fixture_measurements_are_reapplied_to_fresh_analysis_lifetimes()
    {
        var workspace = new RuleTestWorkspace([]);
        workspace.AddProject("Product", [new("Calls.cs", Source)]);
        workspace.WithCoverage(facts => facts.ForFile("Calls.cs").Lines(4, 4));
        var rules = new RuleSet();
        rules
            .For(Code.Projects)
            .Require(global::DrillPress.Coverage.Line.AtLeast(100), "LINES", "Exercise source.");

        var first = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);
        var second = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Empty(first.Findings);
        Assert.Empty(second.Findings);
    }

    [Fact]
    public void Unknown_facts_require_defined_reasons()
    {
        var workspace = new RuleTestWorkspace([]);
        var project = workspace.AddProject("Product", [new("Calls.cs", Source)]);

        var absent = Assert.Throws<ArgumentException>(() =>
            workspace.WithCoverage(facts =>
                facts.ForCall(project, "Calls.cs", "Hit()", 0).Unknown()
            )
        );
        var invalid = Assert.Throws<ArgumentException>(() =>
            workspace.WithCoverage(facts =>
                facts.ForCall(project, "Calls.cs", "Hit()", 0).Unknown((CoverageReason)999)
            )
        );

        Assert.Equal("reasons", absent.ParamName);
        Assert.Equal("reasons", invalid.ParamName);
    }

    private static RuleSet ExecutionRules()
    {
        var rules = new RuleSet();
        rules
            .For(Code.Calls.Where(call => call.Target.Name == "Hit"))
            .Require(global::DrillPress.Coverage.Executed, "CALL", "Exercise call.");
        return rules;
    }

    [Fact]
    public async Task Facts_reject_changed_source_identity_before_rule_evaluation()
    {
        var workspace = new RuleTestWorkspace([]);
        var project = workspace.AddProject("Product", [new("Calls.cs", Source)]);
        workspace.WithCoverage(facts => facts.ForCall(project, "Calls.cs", "Hit()", 0).Executed());
        project.Snapshot.Documents[0] = project.Snapshot.Documents[0] with { Text = Source + " " };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await workspace.CheckAsync(ExecutionRules(), TestContext.Current.CancellationToken)
        );

        Assert.Equal(
            "Synthetic coverage no longer matches its captured source membership.",
            error.Message
        );
    }

    [Fact]
    public async Task Bound_call_selection_retains_its_exact_workspace_occurrence()
    {
        var workspace = new RuleTestWorkspace([]);
        workspace.AddProject("Product", [new("Calls.cs", Source)]);
        var call = Code
            .Calls.Where(call => call.Target.Name == "Hit")
            .In(workspace.Analyze(TestContext.Current.CancellationToken))
            .First();
        workspace.WithCoverage(facts => facts.ForCall(call).Executed());

        var result = await workspace.CheckAsync(
            ExecutionRules(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(50, Assert.Single(result.Findings).Column);
        Assert.Equal("coverage: unknown (loose-source)", Assert.Single(result.Findings).Evidence);
    }
}
