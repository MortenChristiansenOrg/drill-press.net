using System.IO.Abstractions.TestingHelpers;
using DrillPress.Engine;
using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Engine;

public sealed class RuleApplicationTests
{
    [Fact]
    public void Public_construction_requires_no_external_dependencies()
    {
        var type = typeof(RuleApplication);

        var constructors = type.GetConstructors();

        Assert.Equal([0], constructors.Select(constructor => constructor.GetParameters().Length));
    }

    private readonly MockFileSystem _fileSystem = new();

    [Fact]
    public async Task Returns_clean_without_output_for_a_compliant_snapshot()
    {
        var snapshot = TestSnapshots.Create(
            "namespace Sample; public sealed class Target { public static Target Value => null; }");
        const string path = "snapshot.json";
        await new CompilationSnapshotFile(_fileSystem).WriteAsync(path, snapshot, TestContext.Current.CancellationToken);
        var output = new StringWriter();

        var exitCode = await new RuleApplication(_fileSystem).RunAsync(
            RuleTestData.TargetEmptyRuleSet(),
            ["check", path],
            output,
            TextWriter.Null,
            TestContext.Current.CancellationToken);

        Assert.Equal(RuleExitCode.Clean, exitCode);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task Returns_findings_and_writes_each_rule_description_once()
    {
        var directory = _fileSystem.Path.GetFullPath("virtual-project");
        _fileSystem.Directory.CreateDirectory(directory);
        _fileSystem.Directory.SetCurrentDirectory(directory);
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
            _fileSystem.Path.Combine(directory, "Violations.cs"));
        const string path = "snapshot.json";
        await new CompilationSnapshotFile(_fileSystem).WriteAsync(path, snapshot, TestContext.Current.CancellationToken);
        var output = new StringWriter();

        var exitCode = await new RuleApplication(_fileSystem).RunAsync(
            RuleTestData.TargetEmptyRuleSet(),
            ["check", path],
            output,
            TextWriter.Null,
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
    public async Task Invalid_snapshot_files_report_failure_without_diagnostics()
    {
        _fileSystem.AddFile("snapshot.json", new MockFileData(
            """{"fileIdentifier":"drillpress-compilation","formatVersion":-1,"projects":[]}"""));
        var output = new StringWriter();
        var error = new StringWriter();

        var result = await new RuleApplication(_fileSystem).RunAsync(
            RuleTestData.TargetEmptyRuleSet(), ["check", "snapshot.json"],
            output, error, TestContext.Current.CancellationToken);

        Assert.Equal(RuleExitCode.Failure, result);
        Assert.Equal("", output.ToString());
        Assert.Equal(
            $"drillpress-rules: Compilation snapshot format -1 is not supported; expected 1.{Environment.NewLine}",
            error.ToString());
    }

    [Fact]
    public async Task Returns_failure_and_usage_for_invalid_arguments()
    {
        var error = new StringWriter();

        var exitCode = await new RuleApplication(_fileSystem).RunAsync(
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
