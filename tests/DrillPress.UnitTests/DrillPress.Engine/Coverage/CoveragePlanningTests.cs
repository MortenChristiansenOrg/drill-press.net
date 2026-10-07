using System.IO.Abstractions.TestingHelpers;
using System.Text;
using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Engine.Coverage;

public sealed class CoveragePlanningTests
{
    [Fact]
    public async Task Shared_explicit_collection_scope_survives_clause_reporting_configuration()
    {
        var fixture = new CoverageFixture();
        var rules = new RuleCatalog();
        var policy = rules.Rule("SHARED", "Selected covered call.");
        policy
            .For(
                Code.Calls.Where(call =>
                    call.Target.Name == "Hit"
                    && global::DrillPress.Coverage.Executed.ExecutionOf(call)
                        == ExecutionCoverage.Covered
                )
            )
            .CollectCoverageIn(
                Code.Projects.Where(project => project.Name == fixture.Snapshot.Projects[0].Name)
            )
            .ReportOncePer(call => call.Target.Name)
            .Forbid();
        policy.For(Code.Enumerations).Require(global::DrillPress.Coverage.EnumerationStarted);

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(["SHARED"], diagnostics.Select(diagnostic => diagnostic.Descriptor.Id));
        Assert.Equal(1, fixture.Process.Collections);
    }

    [Fact]
    public async Task Candidates_reported_in_generated_source_launch_no_tests()
    {
        var fixture = new CoverageFixture(CoverageFixture.Source, ("Generated.g.cs", "class G {}"));
        fixture.Process.Fail = true;
        var project = fixture.Snapshot.Projects[0];
        var generated = project.Documents[1];
        var snapshot = CompilationSnapshot.Create(
            project with
            {
                Documents =
                [
                    project.Documents[0],
                    SourceIdentity.Capture(
                        new DocumentSnapshot(generated.Path, generated.Text, true),
                        Encoding.UTF8.GetBytes(generated.Text),
                        "utf-8",
                        false
                    ),
                ],
            }
        );
        var rules = new RuleCatalog();
        rules
            .Rule("GENERATED", "Exercise call.")
            .For(Code.Calls.ToMethodsNamed("Hit"))
            .ReportAt(call => new CodeFile(
                call.Source.Project.Sources.Single(source => source.Document.IsGenerated)
            ))
            .Require(global::DrillPress.Coverage.Executed);

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, snapshot, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
        Assert.Empty(fixture.Process.Calls);
    }

    [Fact]
    public async Task Shared_clauses_with_no_candidates_launch_no_tests()
    {
        var fixture = new CoverageFixture();
        fixture.Process.Fail = true;
        var rules = new RuleCatalog();
        var policy = rules.Rule("SHARED", "Exercise selected queries.");
        policy
            .For(Code.Calls.ToMethodsNamed("Missing").Expressions())
            .Require(global::DrillPress.Coverage.Executed);
        policy.For(Code.Enumerations).Require(global::DrillPress.Coverage.EnumerationStarted);
        rules.Rule("STYLE", "Use another API.").For(Code.Calls.ToMethodsNamed("Hit")).Forbid();

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(["STYLE"], diagnostics.Select(diagnostic => diagnostic.Descriptor.Id));
        Assert.Empty(fixture.Process.Calls);
    }

    [Fact]
    public async Task Shared_clauses_plan_only_contexts_with_selected_source_candidates()
    {
        var fixture = new CoverageFixture();
        fixture.Process.State = "no";
        var target = fixture.Snapshot.Projects[0];
        var unrelatedPath = fixture.FileSystem.Path.GetFullPath("/coverage/Unrelated.cs");
        var unrelatedText = CoverageFixture.Source.Replace("Hit", "Other");
        var unrelated = target with
        {
            ContextId = "unrelated",
            Name = "Unrelated",
            AssemblyName = "Unrelated",
            ProjectPath = fixture.FileSystem.Path.GetFullPath("/coverage/Unrelated.csproj"),
            Documents =
            [
                SourceIdentity.Capture(
                    new DocumentSnapshot(unrelatedPath, unrelatedText, false),
                    Encoding.UTF8.GetBytes(unrelatedText),
                    "utf-8",
                    false
                ),
            ],
        };
        fixture.FileSystem.AddFile(unrelated.ProjectPath, new MockFileData("<Project />"));
        fixture.FileSystem.AddFile(unrelatedPath, new MockFileData(unrelatedText));
        var snapshot = CompilationSnapshot.Create([target, unrelated]);
        var rules = new RuleCatalog();
        var policy = rules.Rule("SHARED", "Exercise selected queries.");
        policy
            .For(Code.Calls.ToMethodsNamed("Hit").Expressions())
            .Require(global::DrillPress.Coverage.Executed);
        policy.For(Code.Enumerations).Require(global::DrillPress.Coverage.EnumerationStarted);

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["coverage: uncovered"],
            diagnostics.Select(diagnostic => diagnostic.Evidence)
        );
        Assert.Equal(1, fixture.Process.Collections);
    }

    [Fact]
    public async Task Keyed_exclusion_retains_candidates_when_projected_counterparts_need_coverage()
    {
        var fixture = new CoverageFixture();
        fixture.Process.State = "no";
        var hits = Code.Calls.Where(call => call.Target.Name == "Hit");
        RuleCondition<CodeInvocation> executed = global::DrillPress.Coverage.Executed;
        var counterparts = hits.Where(executed).Select(call => call);
        var rules = new RuleCatalog();
        rules
            .Rule("COVERAGE", "Exercise call.")
            .For(
                hits.WithoutMatching(
                    counterparts,
                    call => call.Location.Start,
                    call => call.Location.Start
                )
            )
            .Require(global::DrillPress.Coverage.Executed);

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["coverage: uncovered"],
            diagnostics.Select(diagnostic => diagnostic.Evidence)
        );
        Assert.Equal(1, fixture.Process.Collections);
    }

    [Fact]
    public async Task Relationship_exclusion_retains_candidates_when_counterparts_need_coverage()
    {
        var fixture = new CoverageFixture();
        fixture.Process.State = "no";
        var hits = Code.Calls.Where(call => call.Target.Name == "Hit");
        RuleCondition<CodeInvocation> executed = global::DrillPress.Coverage.Executed;
        var rules = new RuleCatalog();
        rules
            .Rule("COVERAGE", "Exercise call.")
            .For(
                hits.WithoutMatching(
                    hits.Where(executed),
                    (left, right) => left.Location == right.Location
                )
            )
            .Require(global::DrillPress.Coverage.Executed);

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["coverage: uncovered"],
            diagnostics.Select(diagnostic => diagnostic.Evidence)
        );
        Assert.Equal(1, fixture.Process.Collections);
    }

    [Fact]
    public async Task Certain_alternative_counterparts_exclude_candidates_without_collecting_tests()
    {
        var fixture = new CoverageFixture();
        fixture.Process.Fail = true;
        var hits = Code.Calls.Where(call => call.Target.Name == "Hit");
        RuleCondition<CodeInvocation> executed = global::DrillPress.Coverage.Executed;
        var counterparts = hits.Where(new RuleCondition<CodeInvocation>(_ => true).Or(executed))
            .Select(call => call);
        var rules = new RuleCatalog();
        rules
            .Rule("KEY", "Exercise call.")
            .For(hits.WithoutMatching(counterparts, call => call.Location, call => call.Location))
            .Require(global::DrillPress.Coverage.Executed);
        rules
            .Rule("REL", "Exercise call.")
            .For(
                hits.WithoutMatching(counterparts, (left, right) => left.Location == right.Location)
            )
            .Require(global::DrillPress.Coverage.Executed);

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
        Assert.Empty(fixture.Process.Calls);
    }

    [Fact]
    public async Task Nested_exclusions_do_not_promote_possible_counterparts_to_certain_matches()
    {
        var fixture = new CoverageFixture();
        fixture.Process.State = "no";
        var hits = Code.Calls.Where(call => call.Target.Name == "Hit");
        RuleCondition<CodeInvocation> executed = global::DrillPress.Coverage.Executed;
        var uncertain = hits.WithoutMatching(
            hits.Where(executed),
            call => call.Location,
            call => call.Location
        );
        var rules = new RuleCatalog();
        rules
            .Rule("UNMATCHED", "Unexpected unmatched call.")
            .For(hits.WithoutMatching(uncertain, (left, right) => left.Location == right.Location))
            .Forbid();

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
        Assert.Equal(1, fixture.Process.Collections);
    }

    [Fact]
    public async Task Zero_source_candidates_launch_no_coverage_processes_while_ordinary_rules_still_run()
    {
        var fixture = new CoverageFixture();
        fixture.Process.Fail = true;
        var rules = new RuleCatalog();
        rules
            .Rule("COVERAGE", "Exercise call.")
            .For(Code.Calls.Where(call => call.Target.Name == "Missing"))
            .Require(global::DrillPress.Coverage.Executed);
        rules
            .Rule("STYLE", "Use another API.")
            .For(Code.Calls.Where(call => call.Target.Name == "Hit"))
            .Forbid();

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(["STYLE"], diagnostics.Select(diagnostic => diagnostic.Descriptor.Id));
        Assert.Empty(fixture.Process.Calls);
    }

    [Fact]
    public async Task A_project_outside_the_selected_project_scope_launches_no_coverage_processes()
    {
        var fixture = new CoverageFixture();
        var rules = new RuleCatalog();
        rules
            .Rule("COVERAGE", "Exercise call.")
            .For(Code.Calls.InProject("Product.Data"))
            .Require(global::DrillPress.Coverage.Executed);

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
        Assert.Empty(fixture.Process.Calls);
    }

    [Fact]
    public async Task Unrelated_analysis_targets_do_not_prepare_symbols_or_collect_tests()
    {
        var fixture = new CoverageFixture();
        var target = fixture.Snapshot.Projects[0];
        var otherPath = fixture.FileSystem.Path.GetFullPath("/coverage/Other.csproj");
        var otherSource = fixture.FileSystem.Path.GetFullPath("/coverage/Other.cs");
        var other = target with
        {
            ContextId = "other",
            Name = "Other",
            AssemblyName = "Other",
            ProjectPath = otherPath,
            Documents =
            [
                target.Documents[0] with
                {
                    DocumentId = "other-doc",
                    Path = otherSource,
                    FileIdentity = otherSource,
                },
            ],
        };
        fixture.FileSystem.AddFile(otherPath, new MockFileData("<Project />"));
        fixture.FileSystem.AddFile(otherSource, new MockFileData(CoverageFixture.Source));
        var snapshot = CompilationSnapshot.Create(target, other);
        var rules = new RuleCatalog();
        rules
            .Rule("COVERAGE", "Exercise call.")
            .For(Code.Calls.InProject(target.Name).Where(call => call.Target.Name == "Hit"))
            .Require(global::DrillPress.Coverage.Executed);

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, snapshot, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
        Assert.Equal(1, fixture.Process.Collections);
        Assert.Equal(
            [target.ProjectPath],
            fixture
                .Process.Calls.Where(call => call.Contains("-getProperty:TargetPath"))
                .Select(call => call[1])
        );
    }

    [Fact]
    public async Task Coverage_predicates_projections_and_unions_keep_dependencies_without_poisoning_query_caches()
    {
        var fixture = new CoverageFixture();
        RuleCondition<CodeInvocation> executed = global::DrillPress.Coverage.Executed;
        var covered = Code
            .Calls.Where(executed.And(new(call => call.Target.Name == "Hit")))
            .Select(call => call)
            .Union(Code.Calls.Where(new RuleCondition<CodeInvocation>(call => false).And(executed)))
            .Select(call => (Call: call, call.Target.Name));
        var rules = new RuleCatalog();
        rules
            .Rule("COVERED", "Selected covered call.")
            .For(covered)
            .ReportAt(pair => pair.Call)
            .Forbid();

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(["COVERED"], diagnostics.Select(diagnostic => diagnostic.Descriptor.Id));
        Assert.Equal(1, fixture.Process.Collections);
    }

    [Fact]
    public async Task Negated_and_alternative_coverage_conditions_are_not_evaluated_as_missing_evidence_during_planning()
    {
        var fixture = new CoverageFixture();
        RuleCondition<CodeInvocation> executed = global::DrillPress.Coverage.Executed;
        var rules = new RuleCatalog();
        rules
            .Rule("SELECTED", "Selected call.")
            .For(
                Code.Calls.Where(
                    new RuleCondition<CodeInvocation>(call => call.Target.Name == "Hit").And(
                        executed.Not().Or(executed)
                    )
                )
            )
            .Forbid();

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(["SELECTED"], diagnostics.Select(diagnostic => diagnostic.Descriptor.Id));
        Assert.Equal(1, fixture.Process.Collections);
    }

    [Fact]
    public async Task Explicit_collection_scope_supports_custom_callbacks_that_read_coverage()
    {
        var fixture = new CoverageFixture();
        var rules = new RuleCatalog();
        rules
            .Rule("COVERED", "Selected covered call.")
            .For(
                Code.Calls.Where(call =>
                    call.Target.Name == "Hit"
                    && global::DrillPress.Coverage.Executed.ExecutionOf(call)
                        == ExecutionCoverage.Covered
                )
            )
            .CollectCoverageIn(
                Code.Projects.Where(project => project.Name == fixture.Snapshot.Projects[0].Name)
            )
            .ReportOncePer(call => call.Target.Name)
            .Forbid();

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(["COVERED"], diagnostics.Select(diagnostic => diagnostic.Descriptor.Id));
        Assert.Equal(1, fixture.Process.Collections);
    }

    [Fact]
    public async Task Project_line_requirements_collect_for_their_selected_context()
    {
        var fixture = new CoverageFixture();
        var rules = new RuleCatalog();
        rules
            .Rule("LINES", "Exercise project.")
            .For(Code.Projects)
            .Require(global::DrillPress.Coverage.Line.AtLeast(100));

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
        Assert.Equal(1, fixture.Process.Collections);
    }

    [Fact]
    public async Task File_line_requirements_collect_for_their_selected_context()
    {
        var fixture = new CoverageFixture();
        var rules = new RuleCatalog();
        rules
            .Rule("LINES", "Exercise file.")
            .For(Code.Files)
            .Require(global::DrillPress.Coverage.Line.AtLeast(100));

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
        Assert.Equal(1, fixture.Process.Collections);
    }

    [Fact]
    public async Task Method_line_requirements_collect_for_their_selected_context()
    {
        var fixture = new CoverageFixture();
        var rules = new RuleCatalog();
        rules
            .Rule("LINES", "Exercise method.")
            .For(Code.Methods.Where(method => method.Name == "Run"))
            .Require(global::DrillPress.Coverage.Line.AtLeast(100));

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
        Assert.Equal(1, fixture.Process.Collections);
    }
}
