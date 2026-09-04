using DrillPress.Engine;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Engine;

public sealed class RuleApplicationTests : SnapshotTest
{
    [Fact]
    public async Task Returns_clean_without_output_for_a_compliant_snapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotPath = await WriteSnapshotAsync(
            Path.Combine(Path.GetTempPath(), "drillpress-tests", "Clean.cs"),
            "public static class Clean { public static string Value => \"\"; }",
            cancellationToken);
        var output = new StringWriter();

        var exitCode = await RuleApplication.RunAsync(
            RuleTestData.StringEmptyRuleSet(),
            ["check", snapshotPath],
            output,
            TextWriter.Null,
            cancellationToken);

        Assert.Equal(RuleExitCode.Clean, exitCode);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task Returns_findings_and_writes_each_rule_description_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotPath = await WriteSnapshotAsync(
            Path.Combine(Path.GetTempPath(), "drillpress-tests", "Violations.cs"),
            """
            public static class Violations
            {
                public static string First => string.Empty;
                public static string Second => string.Empty;
            }
            """,
            cancellationToken);
        var output = new StringWriter();

        var exitCode = await RuleApplication.RunAsync(
            RuleTestData.StringEmptyRuleSet(),
            ["check", snapshotPath],
            output,
            TextWriter.Null,
            cancellationToken);
        var text = output.ToString();

        Assert.Equal(RuleExitCode.Findings, exitCode);
        Assert.Equal(1, text.Split("DP1004", StringSplitOptions.None).Length - 1);
        Assert.Contains("  3:", text);
        Assert.Contains("  4:", text);
    }

    [Fact]
    public async Task Returns_failure_and_usage_for_invalid_arguments()
    {
        var error = new StringWriter();

        var exitCode = await RuleApplication.RunAsync(
            RuleTestData.StringEmptyRuleSet(),
            ["check"],
            TextWriter.Null,
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(RuleExitCode.Failure, exitCode);
        Assert.Contains("Usage:", error.ToString());
    }

    [Fact]
    public async Task Returns_failure_and_error_for_an_unreadable_snapshot()
    {
        var error = new StringWriter();
        var missingSnapshot = CreateTemporaryPath(".missing");

        var exitCode = await RuleApplication.RunAsync(
            RuleTestData.StringEmptyRuleSet(),
            ["check", missingSnapshot],
            TextWriter.Null,
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(RuleExitCode.Failure, exitCode);
        Assert.NotEqual(string.Empty, error.ToString());
    }
}
