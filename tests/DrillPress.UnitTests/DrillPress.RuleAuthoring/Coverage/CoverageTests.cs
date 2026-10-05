using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Coverage;

public sealed class CoverageTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Rejects_invalid_percentage_thresholds(double percentage)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            global::DrillPress.Coverage.Line.AtLeast(percentage)
        );
    }

    [Fact]
    public void Direct_evaluation_without_collected_evidence_returns_unknown()
    {
        var reference = RuleTestData.Reference<string>("Empty");
        var rules = new RuleSet();
        rules
            .For(Code.MemberReferences)
            .Require(global::DrillPress.Coverage.Executed, "COV001", "Exercise reference.");

        var diagnostics = rules.Evaluate([reference]);

        Assert.Equivalent(
            new RuleDiagnostic(new("COV001", "Exercise reference."), reference.Location)
            {
                Evidence = "coverage: unknown (not-prepared)",
                Coverage =
                [
                    new(
                        ExecutionCoverage.Unknown,
                        [CoverageReason.EvidenceNotPrepared],
                        "",
                        "",
                        ""
                    ),
                ],
            },
            Assert.Single(diagnostics),
            strict: true
        );
        Assert.Equal(
            ExecutionCoverage.Unknown,
            global::DrillPress.Coverage.Executed.ExecutionOf(reference)
        );
    }
}
