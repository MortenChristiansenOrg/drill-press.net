using System.IO.Abstractions.TestingHelpers;
using DrillPress.Engine;
using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
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
            "namespace Sample; public sealed class Target { public static Target Value => null; }"
        );
        const string path = "snapshot.json";
        await new CompilationSnapshotFile(_fileSystem).WriteAsync(
            path,
            snapshot,
            TestContext.Current.CancellationToken
        );
        var output = new StringWriter();

        var exitCode = await new RuleApplication(_fileSystem).RunAsync(
            RuleTestData.TargetEmptyRuleCatalog(),
            ["check", path],
            output,
            TextWriter.Null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(RuleExitCode.Clean, exitCode);
        Assert.Equal(
            "",
            new CompactDiagnosticRenderer(_fileSystem).Render(
                BundleResponseProtocol.Read(output.ToString(), snapshot)
            )
        );
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
            _fileSystem.Path.Combine(directory, "Violations.cs")
        );
        const string path = "snapshot.json";
        await new CompilationSnapshotFile(_fileSystem).WriteAsync(
            path,
            snapshot,
            TestContext.Current.CancellationToken
        );
        var output = new StringWriter();

        var exitCode = await new RuleApplication(_fileSystem).RunAsync(
            RuleTestData.TargetEmptyRuleCatalog(),
            ["check", path],
            output,
            TextWriter.Null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(RuleExitCode.Findings, exitCode);
        Assert.Equal(
            """
            TEST001 Do not use Target.Empty.
            Violations.cs
              8:35
              9:36

            """.ReplaceLineEndings("\n"),
            new CompactDiagnosticRenderer(_fileSystem).Render(
                BundleResponseProtocol.Read(output.ToString(), snapshot)
            )
        );
    }

    [Fact]
    public async Task Invalid_snapshot_files_report_failure_without_diagnostics()
    {
        _fileSystem.AddFile(
            "snapshot.json",
            new MockFileData(
                """{"fileIdentifier":"drillpress-compilation","formatVersion":-1,"projects":[]}"""
            )
        );
        var output = new StringWriter();
        var error = new StringWriter();

        var result = await new RuleApplication(_fileSystem).RunAsync(
            RuleTestData.TargetEmptyRuleCatalog(),
            ["check", "snapshot.json"],
            output,
            error,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(RuleExitCode.Failure, result);
        Assert.Equal("", output.ToString());
        Assert.Equal(
            $"drillpress-rules: Compilation snapshot format -1 is not supported; expected 5. Use matching Drill Press components.{Environment.NewLine}",
            error.ToString()
        );
    }

    [Theory]
    [InlineData("check")]
    [InlineData("check", "snapshot.json", "--refresh-coverage", "--skip-coverage")]
    public async Task Returns_failure_and_usage_for_invalid_arguments(params string[] arguments)
    {
        var error = new StringWriter();

        var exitCode = await new RuleApplication(_fileSystem).RunAsync(
            RuleTestData.TargetEmptyRuleCatalog(),
            arguments,
            TextWriter.Null,
            error,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(RuleExitCode.Failure, exitCode);
        Assert.Equal(
            $"Usage: <rule-bundle> check <snapshot> [--baseline <accepted-sources>] [--profile] [--parallel-queries] [--no-optimization] [--refresh-coverage] [--skip-coverage] [--explain-coverage]{Environment.NewLine}",
            error.ToString()
        );
    }

    [Fact]
    public async Task Rules_created_after_loading_compare_against_the_accepted_sources()
    {
        var root = _fileSystem.Path.GetFullPath("/repository");
        _fileSystem.Directory.CreateDirectory(root);
        _fileSystem.Directory.SetCurrentDirectory(root);
        string Source(string name) => _fileSystem.Path.Combine(root, "Library", name);
        var outside = _fileSystem.Path.GetFullPath("/shared/Outside.cs");
        var snapshot = CompilationSnapshot.Create(
            TestSnapshots.CreateProject(Source("Library.cs"), "") with
            {
                Documents =
                [
                    new(Source("Changed.cs"), "public class Changed { }", false),
                    new(Source("Same.cs"), "class Same { }", false),
                    new(Source("Added.cs"), "class Added { }", false),
                    new(Source("Moved.cs"), "public class Moved { }", false),
                    new(outside, "public class Outside { }", false),
                ],
            }
        );
        await new CompilationSnapshotFile(_fileSystem).WriteAsync(
            "snapshot.json",
            snapshot,
            TestContext.Current.CancellationToken
        );
        await new AcceptedSourcesFile(_fileSystem).WriteAsync(
            "accepted.json",
            AcceptedSources.Create(
                root,
                new(Source("Changed.cs"), "internal class Changed { }"u8.ToArray(), false),
                new(Source("Same.cs"), "class Same { }"u8.ToArray(), false),
                new(Source("Old.cs"), "internal class Moved { }"u8.ToArray(), true)
            ),
            TestContext.Current.CancellationToken
        );
        var output = new StringWriter();

        var exitCode = await new RuleApplication(_fileSystem).RunAsync(
            ChangeRules,
            ["check", "snapshot.json", "--baseline", "accepted.json"],
            output,
            TextWriter.Null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(RuleExitCode.Findings, exitCode);
        Assert.Equal(
            """
            BASE001 Review changed files.
            Library/Added.cs
              1
            Library/Changed.cs
              1
            Library/Moved.cs
              1
            BASE002 Do not widen accepted types.
            Library/Changed.cs
              1:14
            Library/Moved.cs
              1:14

            """.ReplaceLineEndings("\n"),
            new CompactDiagnosticRenderer(_fileSystem).Render(
                BundleResponseProtocol.Read(output.ToString(), snapshot)
            )
        );
    }

    [Fact]
    public async Task Rules_created_without_an_accepted_state_receive_no_baseline()
    {
        await new CompilationSnapshotFile(_fileSystem).WriteAsync(
            "snapshot.json",
            TestSnapshots.Create("class Same { }"),
            TestContext.Current.CancellationToken
        );
        var received = new List<SourceBaseline?>();

        var exitCode = await new RuleApplication(_fileSystem).RunAsync(
            baseline =>
            {
                received.Add(baseline);
                return new RuleCatalog();
            },
            ["check", "snapshot.json"],
            TextWriter.Null,
            TextWriter.Null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(RuleExitCode.Clean, exitCode);
        Assert.Equal([null], received);
    }

    [Fact]
    public async Task A_fixed_catalog_does_not_load_the_accepted_sources()
    {
        await new CompilationSnapshotFile(_fileSystem).WriteAsync(
            "snapshot.json",
            TestSnapshots.Create("class Same { }"),
            TestContext.Current.CancellationToken
        );
        var error = new StringWriter();

        var exitCode = await new RuleApplication(_fileSystem).RunAsync(
            RuleTestData.TargetEmptyRuleCatalog(),
            ["check", "snapshot.json", "--baseline", "missing.json"],
            TextWriter.Null,
            error,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(RuleExitCode.Clean, exitCode);
        Assert.Equal("", error.ToString());
    }

    private static RuleCatalog ChangeRules(SourceBaseline? accepted)
    {
        var baseline = accepted!;
        var rules = new RuleCatalog();
        rules.Rule("BASE001", "Review changed files.").For(baseline.ChangedFiles).Forbid();
        rules
            .Rule("BASE002", "Do not widen accepted types.")
            .For(
                Code.Types.Where(type =>
                    baseline.PreviousAccessibility(type.Source.Project, type.Symbol) is { } previous
                    && previous < type.Symbol.DeclaredAccessibility
                )
            )
            .Forbid();
        return rules;
    }
}
