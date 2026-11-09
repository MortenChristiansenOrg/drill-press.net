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
            $"drillpress-rules: Compilation snapshot format -1 is not supported; expected 6. Use matching Drill Press components.{Environment.NewLine}",
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
            $"Usage: <rule-bundle> check <snapshot> [--baseline <accepted-sources>] [--select <rule-id>]... [--ignore <rule-id>]... [--exclude <full-path-glob>]... [--profile] [--parallel-queries] [--no-optimization] [--refresh-coverage] [--skip-coverage] [--explain-coverage]{Environment.NewLine}",
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
                    new(Source("Changed.cs"), "public class Changed { }", false)
                    {
                        EncodingName = "utf-16",
                        HasByteOrderMark = true,
                    },
                    new(Source("Same.cs"), "class Same\r\n{\r\n}", false),
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
                new(Source("Same.cs"), "class Same\n{\n}"u8.ToArray(), false),
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

    [Theory]
    [InlineData(
        new string[0],
        RuleExitCode.Findings,
        """
            EDIT Edit the declaration.
            src/Order.cs
              +4:35
            EMPTY Use Target.Other.
            src/Order.cs
              +4:35
            vendor/Target.cs
              +5:35
              +7:34
            PROJECT Review the project.
            vendor/System.cs
              1
            drillpress-allow-invalid Write allowances as // drillpress-allow RULE-ID[, RULE-ID]: reason, after or directly above the code.
            vendor/Target.cs
              6:5 [missing reason]
            drillpress-allow-unused Remove the listed rules from this allowance; they report nothing in its scope.
            src/Order.cs
              +4:49 [PROJECT]

            """
    )]
    [InlineData(
        new[] { "--select", "EMPTY" },
        RuleExitCode.Findings,
        """
            EMPTY Use Target.Other.
            src/Order.cs
              +4:35
            vendor/Target.cs
              +5:35
              +7:34

            """
    )]
    [InlineData(
        new[] { "--ignore", "EMPTY", "--ignore", "drillpress-allow-unused" },
        RuleExitCode.Findings,
        """
            EDIT Edit the declaration.
            src/Order.cs
              +4:35
            PROJECT Review the project.
            vendor/System.cs
              1
            drillpress-allow-invalid Write allowances as // drillpress-allow RULE-ID[, RULE-ID]: reason, after or directly above the code.
            vendor/Target.cs
              6:5 [missing reason]

            """
    )]
    [InlineData(
        new[] { "--select", "EMPTY", "--select", "PROJECT", "--ignore", "EMPTY" },
        RuleExitCode.Findings,
        """
            PROJECT Review the project.
            vendor/System.cs
              1

            """
    )]
    [InlineData(
        new[] { "--select", "drillpress-allow-invalid" },
        RuleExitCode.Findings,
        """
            drillpress-allow-invalid Write allowances as // drillpress-allow RULE-ID[, RULE-ID]: reason, after or directly above the code.
            vendor/Target.cs
              6:5 [missing reason]

            """
    )]
    public async Task Selected_rules_alone_report_and_allowances_naming_other_rules_stay_quiet(
        string[] selection,
        RuleExitCode expectedExit,
        string expectedOutput
    )
    {
        var snapshot = await WriteSelectionSnapshotAsync(
            """
            namespace Sample;
            public static class Order
            {
                public static Target First => Target.Empty; // drillpress-allow PROJECT: reviewed per release
            }
            """
        );
        var output = new StringWriter();

        var exitCode = await new RuleApplication(_fileSystem).RunAsync(
            SelectionRules(),
            ["check", "snapshot.json", .. selection],
            output,
            TextWriter.Null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            (expectedExit, expectedOutput.ReplaceLineEndings("\n")),
            (
                exitCode,
                new CompactDiagnosticRenderer(_fileSystem).Render(
                    BundleResponseProtocol.Read(output.ToString(), snapshot)
                )
            )
        );
    }

    [Fact]
    public async Task Unknown_selected_rule_ids_fail_and_list_the_known_ids()
    {
        await WriteSelectionSnapshotAsync("namespace Sample;");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await new RuleApplication(_fileSystem).RunAsync(
            SelectionRules(),
            ["check", "snapshot.json", "--select", "EMPTY", "--select", "NOPE", "--ignore", "ALSO"],
            output,
            error,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(RuleExitCode.Failure, exitCode);
        Assert.Equal("", output.ToString());
        Assert.Equal(
            $"drillpress-rules: Rule selection names unknown rule IDs: NOPE, ALSO. Known IDs: EDIT, EMPTY, PROJECT, drillpress-allow-invalid, drillpress-allow-unused.{Environment.NewLine}",
            error.ToString()
        );
    }

    [Theory]
    [InlineData(
        "vendor/**",
        RuleExitCode.Findings,
        """
            EDIT Edit the declaration.
            src/Order.cs
              4:35
                fix withheld: the fix edits or moves a file outside the analysis target, in an excluded project or path, or in generated source
            EMPTY Use Target.Other.
            src/Order.cs
              +4:35
            PROJECT Review the project.
            src/Order.cs
              1

            """
    )]
    [InlineData("**", RuleExitCode.Clean, "")]
    public async Task Excluded_paths_report_nothing_while_their_declarations_still_bind(
        string exclusion,
        RuleExitCode expectedExit,
        string expectedOutput
    )
    {
        var snapshot = await WriteSelectionSnapshotAsync(
            """
            namespace Sample;
            public static class Order
            {
                public static Target First => Target.Empty;
            }
            """
        );
        var root = _fileSystem.Path.GetFullPath("/repo");
        var output = new StringWriter();

        var exitCode = await new RuleApplication(_fileSystem).RunAsync(
            SelectionRules(),
            ["check", "snapshot.json", "--exclude", $"{root}/{exclusion}"],
            output,
            TextWriter.Null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            (expectedExit, expectedOutput.ReplaceLineEndings("\n")),
            (
                exitCode,
                new CompactDiagnosticRenderer(_fileSystem).Render(
                    BundleResponseProtocol.Read(output.ToString(), snapshot),
                    explainFixes: true
                )
            )
        );
    }

    [Fact]
    public async Task A_move_into_an_excluded_path_is_withheld()
    {
        var project = _fileSystem.Path.GetFullPath("/repo/Orders");
        _fileSystem.Directory.CreateDirectory(project);
        _fileSystem.Directory.SetCurrentDirectory(project);
        DocumentSnapshot Document(string name, string text) =>
            SourceIdentity.Capture(
                new DocumentSnapshot(_fileSystem.Path.Combine(project, name), text, false)
                {
                    IsDefaultCompileItem = true,
                },
                System.Text.Encoding.UTF8.GetBytes(text),
                "utf-8",
                false
            );
        var snapshot = CompilationSnapshot.Create(
            TestSnapshots.CreateProject(_fileSystem.Path.Combine(project, "Orders.cs"), "") with
            {
                TargetFramework = "net10.0",
                CompileGlob = new(project, []),
                Documents =
                [
                    Document("System.cs", "namespace System { public class Object { } }"),
                    Document("A.cs", "class A { }"),
                ],
            }
        );
        await new CompilationSnapshotFile(_fileSystem).WriteAsync(
            "snapshot.json",
            snapshot,
            TestContext.Current.CancellationToken
        );
        var rules = new RuleCatalog();
        rules
            .Rule("MOVE", "Move A into excluded.")
            .For(Code.Files.Where(file => file.Name == "A.cs"))
            .Forbid(fix: file => Fix.For(file).MoveTo("excluded/A.cs").Propose());
        var output = new StringWriter();

        var exitCode = await new RuleApplication(_fileSystem).RunAsync(
            rules,
            ["check", "snapshot.json", "--exclude", project + "/excluded/**"],
            output,
            TextWriter.Null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            (
                RuleExitCode.Findings,
                """
                MOVE Move A into excluded.
                A.cs
                  1
                    fix withheld: the fix edits or moves a file outside the analysis target, in an excluded project or path, or in generated source

                """.ReplaceLineEndings("\n")
            ),
            (
                exitCode,
                new CompactDiagnosticRenderer(_fileSystem).Render(
                    BundleResponseProtocol.Read(output.ToString(), snapshot),
                    explainFixes: true
                )
            )
        );
    }

    // The vendored files come first, so project findings land in them unless they are excluded. The vendored
    // declaration has its own findings and an allowance without a reason.
    private async Task<CompilationSnapshot> WriteSelectionSnapshotAsync(string order)
    {
        var root = _fileSystem.Path.GetFullPath("/repo");
        _fileSystem.Directory.CreateDirectory(root);
        _fileSystem.Directory.SetCurrentDirectory(root);
        DocumentSnapshot Document(string path, string text) =>
            SourceIdentity.Capture(
                new DocumentSnapshot(_fileSystem.Path.Combine(root, path), text, false),
                System.Text.Encoding.UTF8.GetBytes(text),
                "utf-8",
                false
            );
        var vendor = Document(
            _fileSystem.Path.Combine("vendor", "Target.cs"),
            """
            namespace Sample;
            public sealed class Target
            {
                public static Target Empty => null!;
                public static Target Other => Empty;
                // drillpress-allow EMPTY
                public static Target Copy => Target.Empty;
            }
            """
        );
        var system = Document(
            _fileSystem.Path.Combine("vendor", "System.cs"),
            """
            namespace System
            {
                public class Object { }
                public class ValueType { }
                public struct Void { }
            }
            """
        );
        var snapshot = CompilationSnapshot.Create(
            TestSnapshots.CreateProject(vendor.Path, vendor.Text) with
            {
                Documents =
                [
                    system,
                    vendor,
                    Document(_fileSystem.Path.Combine("src", "Order.cs"), order),
                ],
            }
        );
        await new CompilationSnapshotFile(_fileSystem).WriteAsync(
            "snapshot.json",
            snapshot,
            TestContext.Current.CancellationToken
        );
        return snapshot;
    }

    private static RuleCatalog SelectionRules()
    {
        var rules = new RuleCatalog();
        var references = CodeType.Named("Sample.Target").Member("Empty").References;
        rules
            .Rule("EMPTY", "Use Target.Other.")
            .For(references)
            .Forbid(fix: reference =>
                SourceChanges.Propose(
                    [
                        SourceChanges.Replace(
                            reference.Source,
                            reference.Syntax.Span,
                            "Target.Other"
                        ),
                    ],
                    _ => true
                )
            );
        rules
            .Rule("EDIT", "Edit the declaration.")
            .For(references.Where(reference => reference.Source.Document.Path.EndsWith("Order.cs")))
            .Forbid(fix: reference =>
                SourceChanges.Propose(
                    [
                        SourceChanges.Replace(
                            reference.Source.Project.Sources.First(),
                            new(0, 0),
                            "// edited\n"
                        ),
                    ],
                    _ => true
                )
            );
        rules.Rule("PROJECT", "Review the project.").For(Code.Projects).Forbid();
        return rules;
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
