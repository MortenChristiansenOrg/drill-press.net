using DrillPress.Testing;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Rules;

public sealed class RuleCatalogTests
{
    private const string Prelude = """
        namespace System
        {
            public class Object { }
            public class ValueType { }
            public struct Void { }
        }
        """;

    [Fact]
    public void Parallel_preparation_shares_queries_and_keeps_fix_factories_in_registration_order()
    {
        var initial = RuleTestData.Solution(("A.cs", ["First"]));
        var solution = new AnalysisSolution(
            initial.Projects,
            new AnalysisOptions { QueryPreparationConcurrency = 4 }
        );
        var selections = 0;
        var reports = new List<string>();
        var query = CodeQuery<MemberReference>.Create(solution =>
        {
            Interlocked.Increment(ref selections);
            return Code.MemberReferences.In(solution);
        });
        var rules = new RuleCatalog();
        rules
            .Rule("Z002", "Registered first.")
            .For(query)
            .Forbid(fix: reference =>
            {
                reports.Add("Z002");
                return null;
            });
        rules
            .Rule("A001", "Registered second.")
            .For(query)
            .Forbid(fix: reference =>
            {
                reports.Add("A001");
                return null;
            });

        var diagnostics = rules.Evaluate(solution);

        Assert.Equal(1, selections);
        Assert.Equal(["Z002", "A001"], reports);
        Assert.Equal(
            [("A001", "A.cs", "Sample.Target.First"), ("Z002", "A.cs", "Sample.Target.First")],
            diagnostics.Select(RuleTestData.Describe)
        );
    }

    [Fact]
    public void Parallel_preparation_defers_later_query_failures_until_ordered_evaluation()
    {
        var initial = RuleTestData.Solution(("A.cs", ["First"]));
        var solution = new AnalysisSolution(
            initial.Projects,
            new AnalysisOptions { QueryPreparationConcurrency = 4 }
        );
        var rules = new RuleCatalog();
        rules
            .Rule("FIRST", "First.")
            .For(Code.MemberReferences)
            .Forbid(fix: reference => throw new InvalidOperationException("First factory failed."));
        rules
            .Rule("LATER", "Later.")
            .For(
                CodeQuery<MemberReference>.Create(_ =>
                    throw new InvalidOperationException("Later selector failed.")
                )
            )
            .Forbid();

        var exception = Assert.Throws<InvalidOperationException>(() => rules.Evaluate(solution));

        Assert.Equal("First factory failed.", exception.Message);
    }

    [Fact]
    public void Evaluate_orders_diagnostics_by_rule_path_and_source_position()
    {
        var rules = new RuleCatalog();
        rules.Rule("Z002", "Later rule.").For(Code.MemberReferences).Forbid();
        rules.Rule("A001", "Earlier rule.").For(Code.MemberReferences).Forbid();
        var solution = RuleTestData.Solution(("B.cs", ["Second"]), ("A.cs", ["First", "Any"]));

        var diagnostics = rules.Evaluate(solution);

        Assert.Equal(
            [
                ("A001", "A.cs", "Sample.Target.First"),
                ("A001", "A.cs", "Sample.Target.Any"),
                ("A001", "B.cs", "Sample.Target.Second"),
                ("Z002", "A.cs", "Sample.Target.First"),
                ("Z002", "A.cs", "Sample.Target.Any"),
                ("Z002", "B.cs", "Sample.Target.Second"),
            ],
            diagnostics.Select(RuleTestData.Describe)
        );
    }

    [Fact]
    public void Rule_rejects_duplicate_rule_ids()
    {
        var rules = new RuleCatalog();
        rules.Rule("TEST001", "First message.").For(Code.MemberReferences).Forbid();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            rules.Rule("TEST001", "Second message.")
        );

        Assert.Equal("Rule id 'TEST001' is registered more than once.", exception.Message);
    }

    [Theory]
    [InlineData("", "Message")]
    [InlineData("TEST001", "")]
    [InlineData("TEST\n001", "Message")]
    [InlineData("TEST001", "Message\rnext")]
    [InlineData("TEST001", "Message\u2028next")]
    public void Rule_rejects_blank_or_multiline_rule_identity_or_message(string id, string message)
    {
        var rules = new RuleCatalog();

        Assert.Throws<ArgumentException>(() => rules.Rule(id, message));
    }

    [Theory]
    [InlineData("drillpress-allow-unused")]
    [InlineData("DrillPress-Custom")]
    public void Rule_reserves_the_built_in_rule_prefix(string id)
    {
        var rules = new RuleCatalog();

        var exception = Assert.Throws<ArgumentException>(() => rules.Rule(id, "Message."));

        Assert.Equal(
            "Rule identifiers starting with 'drillpress-' are reserved for built-in rules. (Parameter 'descriptor')",
            exception.Message
        );
    }

    [Fact]
    public void A_rule_without_a_terminated_clause_fails_evaluation_with_guidance()
    {
        var rules = new RuleCatalog();
        rules.Rule("TEST001", "Forgotten.").For(Code.MemberReferences);
        var solution = RuleTestData.Solution();

        var exception = Assert.Throws<InvalidOperationException>(() => rules.Evaluate(solution));

        Assert.Equal(
            "Rule 'TEST001' has no clause; end each For(...) with Forbid(...) or Require(...).",
            exception.Message
        );
    }

    [Fact]
    public void Cancelled_analysis_stops_even_when_no_candidates_exist()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var solution = new AnalysisSolution([], cancellation.Token);
        var rules = new RuleCatalog();
        rules.Rule("TEST001", "Stop evaluation.").For(Code.MemberReferences).Forbid();

        Assert.Throws<OperationCanceledException>(() => rules.Evaluate(solution));
    }

    [Fact]
    public void Invalid_complexity_is_rejected_at_registration()
    {
        var rules = new RuleCatalog();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            rules.Rule("R", "Message.", fixComplexity: (RuleFixComplexity)99)
        );

        Assert.Equal("descriptor", exception.ParamName);
    }

    [Fact]
    public async Task Excluded_projects_report_nothing_while_their_code_still_informs_other_projects()
    {
        var workspace = new RuleTestWorkspace([]);
        var emulator = workspace.AddProject(
            "Contoso.ApiEmulator",
            [
                new("System.cs", Prelude),
                new(
                    "Clock.cs",
                    "public class Clock { public static void Hit() { } void Tick() { Hit(); } }"
                ),
            ]
        );
        workspace.AddProject(
            "Contoso.ApiEmulator.Tests",
            [new("ClockTests.cs", "class ClockTests { void Ticks() { Clock.Hit(); } }")],
            isTest: true,
            dependencies: [emulator]
        );
        workspace.AddProject(
            "Contoso.Orders",
            [new("Orders.cs", "class Orders { void Place() { Clock.Hit(); } }")],
            dependencies: [emulator]
        );
        var rules = PairRules();
        rules.ExcludeProjects("Contoso.ApiEmulator", "Contoso.ApiEmulator.*");

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            CALL Do not call a type's Hit from outside it.
            Orders.cs
              1:37
            HIT Do not call Hit.
            Orders.cs
              1:37

            """.ReplaceLineEndings("\n"),
            result.Output
        );
    }

    [Fact]
    public async Task A_linked_file_still_reports_and_fixes_for_projects_that_are_not_excluded()
    {
        var workspace = new RuleTestWorkspace([]);
        var shared = new TestSource(
            "Shared.cs",
            "class Shared { static void Hit() { } static void Run() { } void M() { Hit(); } }"
        );
        workspace.AddProject("Contoso.ApiEmulator", [new("System.cs", Prelude), shared]);
        workspace.AddProject("Contoso.Orders", [new("System.cs", Prelude), shared]);
        var rules = new RuleCatalog();
        rules
            .Rule("HIT", "Call Run instead of Hit.")
            .For(Code.Calls.Where(call => call.Target.Name == "Hit"))
            .Forbid(fix: call =>
                SourceChanges.Propose(
                    [SourceChanges.Replace(call.Source, call.Operation.Syntax.Span, "Run()")],
                    _ => true
                )
            );
        rules.ExcludeProjects("Contoso.ApiEmulator");

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            HIT Call Run instead of Hit.
            Shared.cs
              +1:71

            """.ReplaceLineEndings("\n"),
            result.Output
        );
        Assert.Equal(
            "class Shared { static void Hit() { } static void Run() { } void M() { Run(); } }",
            result.FixedText("Shared.cs")
        );
    }

    [Fact]
    public async Task A_fix_that_edits_an_excluded_project_is_withheld()
    {
        var workspace = new RuleTestWorkspace([]);
        var emulator = workspace.AddProject(
            "Contoso.ApiEmulator",
            [
                new("System.cs", Prelude),
                new("Clock.cs", "public class Clock { public static void Hit() { } }"),
            ]
        );
        workspace.AddProject(
            "Contoso.Orders",
            [new("Orders.cs", "class Orders { void Place() { Clock.Hit(); } }")],
            dependencies: [emulator]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("SEAL", "Seal the called type.")
            .For(Pairs())
            .ReportAt(pair => pair.Call)
            .Forbid(fix: pair =>
                SourceChanges.Propose(
                    [SourceChanges.Replace(pair.Type.Source, new(0, 0), "sealed ")],
                    _ => true
                )
            );
        rules.ExcludeProjects("Contoso.ApiEmulator");

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            SEAL Seal the called type.
            Orders.cs
              1:37
                fix withheld: the fix edits a file outside the analysis target, in an excluded project, or in generated source

            """.ReplaceLineEndings("\n"),
            result.ExplainedOutput
        );
    }

    [Fact]
    public void Blank_project_exclusions_are_rejected()
    {
        var rules = new RuleCatalog();

        var exception = Assert.Throws<ArgumentException>(() =>
            rules.ExcludeProjects("Contoso.ApiEmulator", " ")
        );

        Assert.Equal("patterns", exception.ParamName);
    }

    [Fact]
    public void Direct_evaluation_omits_findings_in_excluded_projects()
    {
        var workspace = new RuleTestWorkspace([]);
        var emulator = workspace.AddProject(
            "Contoso.ApiEmulator",
            [
                new("System.cs", Prelude),
                new(
                    "Clock.cs",
                    "public class Clock { public static void Hit() { } void Tick() { Hit(); } }"
                ),
            ]
        );
        workspace.AddProject(
            "Contoso.Orders",
            [new("Orders.cs", "class Orders { void Place() { Clock.Hit(); } }")],
            dependencies: [emulator]
        );
        var rules = PairRules();
        rules.ExcludeProjects("Contoso.ApiEmulator");

        var diagnostics = rules.Evaluate(workspace.Analyze(TestContext.Current.CancellationToken));

        Assert.Equal(
            [("CALL", "Orders.cs", 36), ("HIT", "Orders.cs", 36)],
            diagnostics.Select(diagnostic =>
                (diagnostic.Descriptor.Id, diagnostic.Location.FilePath, diagnostic.Location.Start)
            )
        );
    }

    // Pairs each type with the calls to its members from any project, so findings can land in either.
    private static CodeQuery<(CodeTypeDefinition Type, CodeInvocation Call)> Pairs() =>
        CodeQuery<CodeTypeDefinition>
            .Create(solution => solution.Types)
            .Join(
                Code.Calls,
                type => type.Symbol.Name,
                call => call.Target.ContainingType.Name,
                (type, call) => (Type: type, Call: call)
            )
            .Where(pair => pair.Type.Source.Project != pair.Call.Source.Project);

    private static RuleCatalog PairRules()
    {
        var rules = new RuleCatalog();
        rules
            .Rule("HIT", "Do not call Hit.")
            .For(Code.Calls.Where(call => call.Target.Name == "Hit"))
            .Forbid();
        rules
            .Rule("TYPE", "Do not let other projects call Hit.")
            .For(Pairs())
            .ReportAt(pair => pair.Type)
            .Forbid();
        rules
            .Rule("CALL", "Do not call a type's Hit from outside it.")
            .For(Pairs())
            .ReportAt(pair => pair.Call)
            .Forbid();
        return rules;
    }
}
