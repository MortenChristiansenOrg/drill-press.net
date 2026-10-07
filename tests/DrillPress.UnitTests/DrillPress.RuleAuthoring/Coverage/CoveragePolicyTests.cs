using DrillPress.Testing;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Coverage;

public sealed class CoveragePolicyTests
{
    private const string Source = """
        namespace System { public class Object {} public class ValueType {} public struct Void {} }
        class C { static void Hit() {} static void Skip() {} static void Choose() {} void M() { Hit(); Skip(); Choose(); } }
        """;

    [Fact]
    public async Task Different_outcomes_keep_distinct_remediation_and_strict_default_gating()
    {
        var workspace = new RuleTestWorkspace([]);
        workspace.AddProject("Product", [new("Calls.cs", Source)]);
        workspace.WithCoverage(facts =>
            facts
                .ForCall("Calls.cs", "Hit()")
                .Executed()
                .ForCall("Calls.cs", "Skip()")
                .NotExecuted()
                .ForCall("Calls.cs", "Choose()")
                .Unknown(CoverageReason.UnsupportedExpressionMapping)
        );
        var rules = Rules(
            global::DrillPress
                .Coverage.Executed.OnUncovered("Exercise this call.")
                .OnUnknown("Inspect mapping evidence.")
        );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(["Skip()", "Choose()"], result.Findings.Select(finding => finding.Text));
        Assert.Equal(
            ["Exercise this call.", "Inspect mapping evidence."],
            result.Findings.Select(finding => finding.OutcomeRemediation)
        );
        Assert.Equal(
            [FindingDisposition.Violation, FindingDisposition.Violation],
            result.Findings.Select(finding => finding.Disposition)
        );
        Assert.Equal(
            [ExecutionCoverage.Uncovered, ExecutionCoverage.Unknown],
            result.Findings.Select(finding => Assert.Single(finding.Coverage).State)
        );
    }

    [Fact]
    public async Task Explicit_mapping_review_remains_unknown_and_unsatisfied()
    {
        var workspace = new RuleTestWorkspace([]);
        workspace.AddProject("Product", [new("Calls.cs", Source)]);
        workspace.WithCoverage(facts =>
            facts
                .ForCall("Calls.cs", "Hit()")
                .Executed()
                .ForCall("Calls.cs", "Skip()")
                .Executed()
                .ForCall("Calls.cs", "Choose()")
                .Unknown(CoverageReason.UnsupportedExpressionMapping)
        );
        var rules = Rules(
            global::DrillPress
                .Coverage.Executed.ReviewUnknownFor(CoverageReason.UnsupportedExpressionMapping)
                .OnUnknown("Review this mapping.")
        );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        var finding = Assert.Single(result.Findings);
        var evidence = Assert.Single(finding.Coverage);
        Assert.Equal(FindingDisposition.Review, finding.Disposition);
        Assert.Equal("Review this mapping.", finding.OutcomeRemediation);
        Assert.Equal(ExecutionCoverage.Unknown, evidence.State);
        Assert.False(evidence.SatisfiesRequirement);
        Assert.True(evidence.IsReviewEligible);
        Assert.Equal([CoverageReason.UnsupportedExpressionMapping], evidence.ReviewReasons);
    }

    [Fact]
    public async Task Missing_tests_and_mixed_unknown_reasons_remain_violations_under_mapping_review()
    {
        var workspace = new RuleTestWorkspace([]);
        workspace.AddProject("Product", [new("Calls.cs", Source)]);
        workspace.WithCoverage(facts =>
            facts
                .ForCall("Calls.cs", "Hit()")
                .Unknown(CoverageReason.NoApplicableTests)
                .ForCall("Calls.cs", "Skip()")
                .Unknown(CoverageReason.MissingSymbols)
                .ForCall("Calls.cs", "Choose()")
                .Unknown(CoverageReason.UnsupportedExpressionMapping, CoverageReason.PartialRange)
        );
        var rules = Rules(
            global::DrillPress.Coverage.Executed.ReviewUnknownFor(
                CoverageReason.UnsupportedExpressionMapping
            )
        );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                FindingDisposition.Violation,
                FindingDisposition.Violation,
                FindingDisposition.Violation,
            ],
            result.Findings.Select(finding => finding.Disposition)
        );
        Assert.All(
            result.Findings,
            finding => Assert.False(Assert.Single(finding.Coverage).IsReviewEligible)
        );
    }

    [Fact]
    public async Task Composed_source_failures_keep_failure_gating()
    {
        var workspace = new RuleTestWorkspace([]);
        workspace.AddProject("Product", [new("Calls.cs", Source)]);
        workspace.WithCoverage(facts =>
            facts
                .ForCall("Calls.cs", "Choose()")
                .Unknown(CoverageReason.UnsupportedExpressionMapping)
        );
        RuleCondition<CodeInvocation> review =
            global::DrillPress.Coverage.Executed.ReviewUnknownFor(
                CoverageReason.UnsupportedExpressionMapping
            );
        var calls = Code.Calls.Where(call => call.Target.Name == "Choose");
        var rules = new RuleCatalog();
        rules
            .Rule("FAIL", "Verify both requirements.")
            .For(calls)
            .Require(review.And(new(_ => false)));
        rules.Rule("REVIEW", "Verify execution.").For(calls).Require(review.And(new(_ => true)));
        rules
            .Rule("SATISFIED", "Verify either requirement.")
            .For(calls)
            .Require(review.Or(new(_ => true)));

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(["FAIL", "REVIEW"], result.Findings.Select(finding => finding.Rule));
        Assert.Equal(
            [FindingDisposition.Violation, FindingDisposition.Review],
            result.Findings.Select(finding => finding.Disposition)
        );
    }

    [Fact]
    public async Task Operational_collection_failures_never_become_review_findings()
    {
        var fixture = new CoverageFixture();
        fixture.Process.Fail = true;
        var rules = new RuleCatalog();
        rules
            .Rule("CALL", "Verify execution.")
            .For(Code.Calls)
            .Require(
                global::DrillPress.Coverage.Executed.ReviewUnknownFor(
                    CoverageReason.UnsupportedExpressionMapping
                )
            );

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture
                .Engine()
                .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken)
        );

        Assert.Equal("Coverage collection failed: tests failed.", error.Message);
        Assert.Equal(1, fixture.Process.Collections);
    }

    [Fact]
    public void Review_policy_rejects_missing_test_reasons_and_empty_configuration()
    {
        var requirement = global::DrillPress.Coverage.Executed;

        var missingTests = Assert.Throws<ArgumentException>(() =>
            requirement.ReviewUnknownFor(CoverageReason.NoApplicableTests)
        );
        var absent = Assert.Throws<ArgumentException>(() => requirement.ReviewUnknownFor());

        Assert.Equal("reasons", missingTests.ParamName);
        Assert.Equal("reasons", absent.ParamName);
    }

    private static RuleCatalog Rules(CoverageRequirement requirement)
    {
        var rules = new RuleCatalog();
        rules
            .Rule("CALL", "Calls require verified execution.")
            .For(Code.Calls.Where(call => call.Target.Name is "Hit" or "Skip" or "Choose"))
            .Require(requirement);
        return rules;
    }

    [Fact]
    public async Task Reporting_once_keeps_a_real_violation_over_an_earlier_review_item()
    {
        var workspace = new RuleTestWorkspace([]);
        workspace.AddProject("Product", [new("Calls.cs", Source)]);
        workspace.WithCoverage(facts =>
            facts
                .ForCall("Calls.cs", "Hit()")
                .Unknown(CoverageReason.UnsupportedExpressionMapping)
                .ForCall("Calls.cs", "Skip()")
                .NotExecuted()
                .ForCall("Calls.cs", "Choose()")
                .Executed()
        );
        var rules = new RuleCatalog();
        rules
            .Rule("CALL", "Verify execution.")
            .For(Code.Calls.Where(call => call.Target.Name is "Hit" or "Skip" or "Choose"))
            .ReportOncePer(_ => "all")
            .Require(
                global::DrillPress.Coverage.Executed.ReviewUnknownFor(
                    CoverageReason.UnsupportedExpressionMapping
                )
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal("Skip()", Assert.Single(result.Findings).Text);
        Assert.Equal(FindingDisposition.Violation, Assert.Single(result.Findings).Disposition);
        Assert.Equal(
            ExecutionCoverage.Uncovered,
            Assert.Single(Assert.Single(result.Findings).Coverage).State
        );
    }
}
