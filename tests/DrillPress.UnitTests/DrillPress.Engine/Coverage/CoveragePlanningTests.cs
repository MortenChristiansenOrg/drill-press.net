using System.IO.Abstractions.TestingHelpers;
using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Engine.Coverage;

public sealed class CoveragePlanningTests
{
    [Fact]
    public async Task Zero_source_candidates_launch_no_coverage_processes_while_ordinary_rules_still_run()
    {
        var fixture = new CoverageFixture();
        fixture.Process.Fail = true;
        var rules = new RuleSet();
        rules
            .For(Code.Calls.Where(call => call.Target.Name == "Missing"))
            .Require(global::DrillPress.Coverage.Executed, "COVERAGE", "Exercise call.");
        rules
            .For(Code.Calls.Where(call => call.Target.Name == "Hit"))
            .Forbid("STYLE", "Use another API.");

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
        var rules = new RuleSet();
        rules
            .For(Code.Calls.InProject("Product.Data"))
            .Require(global::DrillPress.Coverage.Executed, "COVERAGE", "Exercise call.");

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
        var rules = new RuleSet();
        rules
            .For(Code.Calls.InProject(target.Name).Where(call => call.Target.Name == "Hit"))
            .Require(global::DrillPress.Coverage.Executed, "COVERAGE", "Exercise call.");

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
            .At(call => call);
        var rules = new RuleSet();
        rules.For(covered).Forbid("COVERED", "Selected covered call.");

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
        var rules = new RuleSet();
        rules
            .For(
                Code.Calls.Where(
                    new RuleCondition<CodeInvocation>(call => call.Target.Name == "Hit").And(
                        executed.Not().Or(executed)
                    )
                )
            )
            .Forbid("SELECTED", "Selected call.");

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
        var rules = new RuleSet();
        rules
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
            .Forbid("COVERED", "Selected covered call.");

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
        var rules = new RuleSet();
        rules
            .For(Code.Projects)
            .Require(global::DrillPress.Coverage.Line.AtLeast(100), "LINES", "Exercise project.");

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
        var rules = new RuleSet();
        rules
            .For(Code.Files)
            .Require(global::DrillPress.Coverage.Line.AtLeast(100), "LINES", "Exercise file.");

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
        var rules = new RuleSet();
        rules
            .For(Code.Methods.Where(method => method.Name == "Run"))
            .Require(global::DrillPress.Coverage.Line.AtLeast(100), "LINES", "Exercise method.");

        var diagnostics = await fixture
            .Engine()
            .AnalyzeAsync(rules, fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
        Assert.Equal(1, fixture.Process.Collections);
    }
}
