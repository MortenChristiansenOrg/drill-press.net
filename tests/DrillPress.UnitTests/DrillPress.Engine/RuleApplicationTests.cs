using DrillPress.Engine;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Engine;

public sealed class RuleApplicationTests
{
    [Fact]
    public async Task Returns_clean_without_output_for_a_compliant_snapshot()
    {
        var snapshot = TestSnapshots.Create(
            "namespace Sample; public sealed class Target { public static Target Value => null; }");
        var output = new StringWriter();

        var exitCode = await RuleApplication.RunAsync(
            RuleTestData.TargetEmptyRuleSet(),
            snapshot,
            output,
            TestContext.Current.CancellationToken);

        Assert.Equal(RuleExitCode.Clean, exitCode);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task Returns_findings_and_writes_each_rule_description_once()
    {
        var snapshot = TestSnapshots.Create(
            """
            namespace Sample;
            public sealed class Target
            {
                public static Target Empty => null;
            }
            public static class Violations
            {
                public static Target First => Target.Empty;
                public static Target Second => Target.Empty;
            }
            """,
            "Violations.cs");
        var output = new StringWriter();

        var exitCode = await RuleApplication.RunAsync(
            RuleTestData.TargetEmptyRuleSet(),
            snapshot,
            output,
            TestContext.Current.CancellationToken);

        Assert.Equal(RuleExitCode.Findings, exitCode);
        Assert.Equal(
            """
            TEST001 Do not use Target.Empty.
            Violations.cs
              8:35
              9:36

            """,
            output.ToString());
    }

    [Fact]
    public async Task Returns_failure_and_usage_for_invalid_arguments()
    {
        var error = new StringWriter();

        var exitCode = await RuleApplication.RunAsync(
            RuleTestData.TargetEmptyRuleSet(),
            ["check"],
            TextWriter.Null,
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(RuleExitCode.Failure, exitCode);
        Assert.Equal(
            $"Usage: <rule-bundle> check <snapshot>{Environment.NewLine}",
            error.ToString());
    }
}
