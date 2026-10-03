using DrillPress.Engine;
using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Engine.Coverage;

public sealed class CoverageCollectorTests
{
    [Fact]
    public async Task Collects_once_and_reuses_evidence_across_rules_and_invocations()
    {
        var fixture = new CoverageFixture();
        var rules = CoverageFixture.ExecutionRules();
        rules
            .For(Code.Files)
            .Require(global::DrillPress.Coverage.Line.AtLeast(100), "COV002", "Exercise file.");

        var first = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);
        var second = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.Process.Collections);
        Assert.Equal(first, second);
        Assert.Equal(
            ["coverage: unknown", "coverage: unknown"],
            first.Select(diagnostic => diagnostic.Evidence)
        );
    }

    [Fact]
    public async Task Matching_project_contexts_share_discovery_within_an_analysis()
    {
        var single = new CoverageFixture();
        var multiple = new CoverageFixture();
        var project = multiple.Snapshot.Projects[0];
        single.FileSystem.AddFile("/coverage/Second.csproj", new("<Project />"));
        multiple.FileSystem.AddFile("/coverage/Second.csproj", new("<Project />"));
        single.Process.ReferencingTargets =
        [
            single.Snapshot.Projects[0].ProjectPath,
            multiple.FileSystem.Path.GetFullPath("/coverage/Second.csproj"),
        ];
        multiple.Process.ReferencingTargets =
        [
            project.ProjectPath,
            multiple.FileSystem.Path.GetFullPath("/coverage/Second.csproj"),
        ];
        var snapshot = CompilationSnapshot.Create(
            project,
            project with
            {
                ContextId = "second-context",
                ProjectPath = multiple.FileSystem.Path.GetFullPath("/coverage/Second.csproj"),
                Documents = [project.Documents[0] with { DocumentId = "second-document" }],
            }
        );
        var rules = CoverageFixture.ExecutionRules();

        await single
            .Engine()
            .AnalyzeAsync(rules, single.Snapshot, TestContext.Current.CancellationToken);
        var diagnostics = await multiple
            .Engine()
            .AnalyzeAsync(rules, snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(single.Process.Evaluations, multiple.Process.Evaluations);
        Assert.Equal(1, multiple.Process.Collections);
        Assert.Equal(
            ["coverage: unknown", "coverage: unknown", "coverage: unknown", "coverage: unknown"],
            diagnostics.Select(diagnostic => diagnostic.Evidence)
        );
    }

    [Fact]
    public async Task Bundle_protocol_preserves_stable_remediation_and_renders_each_occurrences_evidence()
    {
        var fixture = new CoverageFixture();
        fixture.Process.StatesByRun.Add(["no", "yes", "yes"]);
        var snapshotPath = "/snapshot.json";
        await new CompilationSnapshotFile(fixture.FileSystem).WriteAsync(
            snapshotPath,
            fixture.Snapshot,
            TestContext.Current.CancellationToken
        );
        var output = new StringWriter();
        var error = new StringWriter();
        var application = new RuleApplication(fixture.FileSystem, fixture.Process);

        var exit = await application.RunAsync(
            CoverageFixture.ExecutionRules(),
            ["check", snapshotPath],
            output,
            error,
            TestContext.Current.CancellationToken
        );
        var plan = BundleResponseProtocol.Read(output.ToString(), fixture.Snapshot);
        var rendered = new CompactDiagnosticRenderer(fixture.FileSystem).Render(plan);

        Assert.Equal(RuleExitCode.Findings, exit);
        Assert.Equal("", error.ToString());
        Assert.Equal(
            """
            COV001 Exercise call.
            coverage/Target.cs
              7:9 [coverage: uncovered]
              9:34 [coverage: unknown]
              9:47 [coverage: unknown]

            """.ReplaceLineEndings("\n"),
            rendered
        );
    }

    [Fact]
    public async Task Changes_to_test_inputs_invalidate_cached_evidence()
    {
        var fixture = new CoverageFixture();
        var rules = CoverageFixture.ExecutionRules();
        await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);
        fixture.FileSystem.AddFile("/coverage/TestData.json", new("changed test data"));

        await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(2, fixture.Process.Collections);
    }

    [Theory]
    [InlineData(".csproj")]
    [InlineData(".fsproj")]
    [InlineData(".vbproj")]
    public async Task Discovers_referencing_dotnet_test_projects_independently_of_test_language(
        string extension
    )
    {
        var fixture = new CoverageFixture();
        fixture.FileSystem.File.Delete("/coverage/Tests.csproj");
        fixture.FileSystem.AddFile(
            fixture.FileSystem.Path.ChangeExtension("/coverage/Tests.csproj", extension),
            new("<Project />")
        );

        await fixture
            .Engine()
            .AnalyzeAsync(
                CoverageFixture.ExecutionRules(),
                fixture.Snapshot,
                TestContext.Current.CancellationToken
            );

        Assert.Equal(1, fixture.Process.Collections);
    }

    [Fact]
    public async Task Unrestored_test_packages_are_discovered_without_imported_test_flags()
    {
        var fixture = new CoverageFixture();
        fixture.Process.Unrestored = true;

        await fixture
            .Engine()
            .AnalyzeAsync(
                CoverageFixture.ExecutionRules(),
                fixture.Snapshot,
                TestContext.Current.CancellationToken
            );

        Assert.Equal(1, fixture.Process.Collections);
    }

    [Fact]
    public async Task An_explicit_test_project_opt_out_is_respected()
    {
        var fixture = new CoverageFixture();
        fixture.Process.TestProjectOptOut = true;

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(
                CoverageFixture.ExecutionRules(),
                fixture.Snapshot,
                TestContext.Current.CancellationToken
            );

        Assert.Equal(0, fixture.Process.Collections);
        Assert.Equal(
            ["coverage: unknown", "coverage: unknown", "coverage: unknown"],
            diagnostics.Select(diagnostic => diagnostic.Evidence)
        );
    }

    [Fact]
    public async Task Composed_coverage_conditions_keep_both_measured_requirements()
    {
        var fixture = new CoverageFixture();
        fixture.Process.State = "partial";
        RuleCondition<CodeFile> minimum = global::DrillPress.Coverage.Line.AtLeast(0);
        RuleCondition<CodeFile> complete = global::DrillPress.Coverage.Line.AtLeast(100);
        var rules = new RuleSet();
        rules.For(Code.Files).Require(minimum.And(complete), "COV001", "Exercise file.");

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(
            "line coverage: 0% (0/3), required 0%; line coverage: 0% (0/3), required 100%",
            Assert.Single(diagnostics).Evidence
        );
    }

    [Fact]
    public async Task Changes_to_external_imported_build_inputs_invalidate_the_cache()
    {
        var fixture = new CoverageFixture();
        fixture.Process.ImportedInputs = ["/external/settings.props"];
        fixture.FileSystem.AddFile("/external/settings.props", new("original"));
        var rules = CoverageFixture.ExecutionRules();
        await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);
        fixture.FileSystem.AddFile("/external/settings.props", new("changed"));

        await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(2, fixture.Process.Collections);
    }

    [Fact]
    public async Task Restored_package_imports_are_included_before_collecting_and_publishing()
    {
        var fixture = new CoverageFixture();
        fixture.Process.RestoredInputs = ["/external/package.targets"];
        fixture.FileSystem.AddFile("/external/package.targets", new("original"));
        var rules = CoverageFixture.ExecutionRules();

        var first = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);
        var second = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(first, second);
        Assert.Equal(1, fixture.Process.Collections);
    }

    [Fact]
    public async Task Changes_to_sources_in_external_project_dependencies_invalidate_the_cache()
    {
        var fixture = new CoverageFixture();
        fixture.Process.ExternalProject = fixture.FileSystem.Path.GetFullPath(
            "/external/Dependency.csproj"
        );
        fixture.Process.ExternalSource = fixture.FileSystem.Path.GetFullPath(
            "/external/Dependency.cs"
        );
        fixture.FileSystem.AddFile("/external/Dependency.csproj", new("<Project />"));
        fixture.FileSystem.AddFile("/external/Dependency.cs", new("original"));
        var rules = CoverageFixture.ExecutionRules();
        await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);
        fixture.FileSystem.AddFile("/external/Dependency.cs", new("changed"));

        await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(2, fixture.Process.Collections);
    }

    [Fact]
    public async Task Inputs_changed_during_collection_are_rejected_and_not_cached()
    {
        var fixture = new CoverageFixture();
        fixture.Process.ImportedInputs = ["/external/package.targets"];
        fixture.FileSystem.AddFile("/external/package.targets", new("original"));
        var timestamp = fixture.FileSystem.File.GetLastWriteTimeUtc("/external/package.targets");
        fixture.Process.DuringCollection = () =>
        {
            fixture.FileSystem.File.WriteAllText("/external/package.targets", "modified");
            fixture.FileSystem.File.SetLastWriteTimeUtc("/external/package.targets", timestamp);
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture
                .Engine()
                .AnalyzeAsync(
                    CoverageFixture.ExecutionRules(),
                    fixture.Snapshot,
                    TestContext.Current.CancellationToken
                )
        );

        Assert.Equal(
            "Coverage inputs changed during test execution; retry analysis.",
            error.Message
        );
        Assert.Empty(
            fixture.FileSystem.Directory.EnumerateFiles(
                new CoverageCache(fixture.FileSystem).Root,
                "*.xml"
            )
        );
    }

    [Fact]
    public async Task Different_compiler_symbols_cannot_reuse_execution_evidence()
    {
        var fixture = new CoverageFixture();
        var snapshot = CompilationSnapshot.Create(
            fixture.Snapshot.Projects[0] with
            {
                PreprocessorSymbols = ["OTHER_FRAMEWORK"],
            }
        );

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(
                CoverageFixture.ExecutionRules(),
                snapshot,
                TestContext.Current.CancellationToken
            );

        Assert.Equal(
            ["coverage: unknown", "coverage: unknown", "coverage: unknown"],
            diagnostics.Select(diagnostic => diagnostic.Evidence)
        );
    }

    [Fact]
    public async Task A_pdb_without_its_associated_assembly_cannot_prove_execution()
    {
        var fixture = new CoverageFixture();
        fixture.FileSystem.File.Delete(
            fixture.FileSystem.Path.ChangeExtension(
                fixture.Snapshot.Projects[0].ProjectPath,
                ".dll"
            )
        );

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(
                CoverageFixture.ExecutionRules(),
                fixture.Snapshot,
                TestContext.Current.CancellationToken
            );

        Assert.Equal(
            ["coverage: unknown", "coverage: unknown", "coverage: unknown"],
            diagnostics.Select(diagnostic => diagnostic.Evidence)
        );
    }

    [Fact]
    public async Task Other_framework_modules_with_the_same_assembly_name_do_not_mask_matching_evidence()
    {
        var fixture = new CoverageFixture();
        fixture.Process.OtherFrameworkModule = true;

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(
                CoverageFixture.ExecutionRules(),
                fixture.Snapshot,
                TestContext.Current.CancellationToken
            );

        Assert.Equal(
            ["coverage: unknown", "coverage: unknown"],
            diagnostics.Select(diagnostic => diagnostic.Evidence)
        );
    }

    [Fact]
    public async Task Explicit_refresh_recollects_matching_inputs()
    {
        var fixture = new CoverageFixture();
        var rules = CoverageFixture.ExecutionRules();
        await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        await fixture
            .Engine()
            .EvaluateAsync(
                rules,
                fixture.Snapshot,
                new AnalysisOptions { RefreshCoverage = true },
                TestContext.Current.CancellationToken
            );

        Assert.Equal(2, fixture.Process.Collections);
    }

    [Fact]
    public async Task Rules_without_coverage_do_not_start_external_processes()
    {
        var fixture = new CoverageFixture();
        fixture.Process.Fail = true;

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(new RuleSet(), fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
        Assert.Equal(0, fixture.Process.Collections);
    }

    [Fact]
    public async Task Collection_failure_is_reported_and_is_not_cached()
    {
        var fixture = new CoverageFixture();
        fixture.Process.Fail = true;
        var rules = CoverageFixture.ExecutionRules();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture
                .Engine()
                .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken)
        );

        Assert.Equal("Coverage collection failed: tests failed.", error.Message);
        Assert.Equal(1, fixture.Process.Collections);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("build")]
    [InlineData("excluded")]
    [InlineData("missing")]
    [InlineData("function")]
    public async Task Invalid_or_missing_evidence_never_satisfies_execution(string invalidEvidence)
    {
        var fixture = new CoverageFixture();
        fixture.Process.Stale = invalidEvidence == "stale";
        fixture.Process.DifferentBuild = invalidEvidence == "build";
        fixture.Process.Excluded = invalidEvidence == "excluded";
        fixture.Process.HasTests = invalidEvidence != "missing";
        fixture.Process.MissingFunctionIdentity = invalidEvidence == "function";

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(
                CoverageFixture.ExecutionRules(),
                fixture.Snapshot,
                TestContext.Current.CancellationToken
            );

        Assert.Equal(
            ["coverage: unknown", "coverage: unknown", "coverage: unknown"],
            diagnostics.Select(diagnostic => diagnostic.Evidence)
        );
    }

    [Fact]
    public async Task Uncovered_occurrences_report_their_exact_call_locations()
    {
        var fixture = new CoverageFixture();
        fixture.Process.State = "no";

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(
                CoverageFixture.ExecutionRules(),
                fixture.Snapshot,
                TestContext.Current.CancellationToken
            );

        Assert.Equal(
            ["Hit()", "HitValue()", "HitValue()"],
            diagnostics.Select(diagnostic =>
                CoverageFixture.Source.Substring(
                    diagnostic.Location.Start,
                    diagnostic.Location.Length
                )
            )
        );
        Assert.Equal(
            ["coverage: uncovered", "coverage: uncovered", "coverage: uncovered"],
            diagnostics.Select(diagnostic => diagnostic.Evidence)
        );
    }

    [Theory]
    [InlineData(true, "line coverage: 0% (0/3), required 100%")]
    [InlineData(false, "line coverage: unknown (missing or zero coverable lines)")]
    public async Task Ambiguous_function_mapping_or_line_only_evidence_is_unknown(
        bool duplicateFunctions,
        string percentageMessage
    )
    {
        var fixture = new CoverageFixture();
        fixture.Process.Ambiguous = duplicateFunctions;
        fixture.Process.LineOnly = !duplicateFunctions;
        var rules = CoverageFixture.ExecutionRules();
        rules
            .For(Code.Files)
            .Require(global::DrillPress.Coverage.Line.AtLeast(100), "COV002", "Exercise file.");

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["coverage: unknown", "coverage: unknown", "coverage: unknown", percentageMessage],
            diagnostics.Select(diagnostic => diagnostic.Evidence)
        );
    }

    [Fact]
    public async Task Ambiguous_sequence_points_within_one_run_cannot_prove_execution()
    {
        var fixture = new CoverageFixture();
        fixture.Process.MixedSequencePoints = true;

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(
                CoverageFixture.ExecutionRules(),
                fixture.Snapshot,
                TestContext.Current.CancellationToken
            );

        Assert.Equal(
            ["coverage: unknown", "coverage: unknown", "coverage: unknown"],
            diagnostics.Select(diagnostic => diagnostic.Evidence)
        );
    }

    [Fact]
    public async Task Direct_member_references_and_other_expressions_use_precise_statement_ranges()
    {
        var fixture = new CoverageFixture(
            """
            namespace System { public class Object {} public struct Void {} public struct Int32 {} }
            class C
            {
                static int Value => 1;
                int Run()
                {
                    var first = Value;
                    var second = 1;
                    return Value;
                }
            }
            """
        );
        var rules = new RuleSet();
        rules
            .For(Code.MemberReferences.Where(reference => reference.MemberName == "Value"))
            .Require(global::DrillPress.Coverage.Executed, "COV001", "Exercise reference.");
        rules
            .For(
                Code.Nodes<Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax>()
                    .Where(node => node.Location.Line == 8)
            )
            .Require(global::DrillPress.Coverage.Executed, "COV002", "Exercise expression.");

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
        Assert.Equal(1, fixture.Process.Collections);
    }

    [Fact]
    public async Task Parentheses_and_checked_expressions_preserve_direct_call_execution()
    {
        var fixture = new CoverageFixture(
            """
            namespace System { public class Object {} public struct Void {} }
            class C
            {
                static C HitValue() => null;
                void Run()
                {
                    var value = (HitValue());
                    var other = checked(HitValue());
                }
            }
            """
        );

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(
                CoverageFixture.ExecutionRules(),
                fixture.Snapshot,
                TestContext.Current.CancellationToken
            );

        Assert.Empty(diagnostics);
        Assert.Equal(1, fixture.Process.Collections);
    }

    [Fact]
    public async Task Merges_matching_test_runs_from_counts_without_double_counting_lines()
    {
        var fixture = new CoverageFixture();
        fixture.FileSystem.AddFile("/coverage/SecondTests.csproj", new("<Project />"));
        fixture.Process.StatesByRun.Add(["yes", "no", "no"]);
        fixture.Process.StatesByRun.Add(["no", "yes", "no"]);
        var rules = new RuleSet();
        rules
            .For(Code.Projects)
            .Require(global::DrillPress.Coverage.Line.AtLeast(90), "COV001", "Exercise project.");

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(2, fixture.Process.Collections);
        Assert.Equal(
            "line coverage: 66.67% (2/3), required 90%",
            Assert.Single(diagnostics).Evidence
        );
    }

    [Fact]
    public async Task Source_bytes_changed_since_the_snapshot_cannot_satisfy_coverage()
    {
        var fixture = new CoverageFixture();
        fixture.FileSystem.AddFile("/coverage/Target.cs", new(CoverageFixture.Source + " "));

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(
                CoverageFixture.ExecutionRules(),
                fixture.Snapshot,
                TestContext.Current.CancellationToken
            );

        Assert.Equal(
            ["coverage: unknown", "coverage: unknown", "coverage: unknown"],
            diagnostics.Select(diagnostic => diagnostic.Evidence)
        );
    }

    [Fact]
    public async Task A_zero_coverable_file_fails_even_a_zero_percent_requirement()
    {
        var fixture = new CoverageFixture();
        fixture.Process.Excluded = true;
        var rules = new RuleSet();
        rules
            .For(Code.Files)
            .Require(global::DrillPress.Coverage.Line.AtLeast(0), "COV001", "Exercise file.");

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(
            "line coverage: unknown (missing or zero coverable lines)",
            Assert.Single(diagnostics).Evidence
        );
    }

    [Fact]
    public async Task Coverage_filters_retain_collection_requirements_through_query_composition()
    {
        var fixture = new CoverageFixture();
        RuleCondition<CodeInvocation> executed = global::DrillPress.Coverage.Executed;
        var query = Code
            .Calls.Where(executed)
            .Select(call => call)
            .Union(Code.Calls.Where(executed));
        var rules = new RuleSet();
        rules.For(query).Forbid("COV001", "Found executed call.");

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.Process.Collections);
        Assert.Equal(
            "Hit()",
            CoverageFixture.Source.Substring(
                Assert.Single(diagnostics).Location.Start,
                diagnostics[0].Location.Length
            )
        );
    }

    [Fact]
    public async Task Partially_covered_lines_are_not_fully_covered_and_include_counts()
    {
        var fixture = new CoverageFixture();
        fixture.Process.State = "partial";
        var rules = new RuleSet();
        rules
            .For(Code.Files)
            .Require(global::DrillPress.Coverage.Line.AtLeast(90), "COV001", "Exercise file.");

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal("line coverage: 0% (0/3), required 90%", Assert.Single(diagnostics).Evidence);
    }
}
