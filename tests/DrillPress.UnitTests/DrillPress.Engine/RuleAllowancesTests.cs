using DrillPress.Engine;
using DrillPress.Manifest;
using DrillPress.Testing;
using Xunit;

namespace DrillPress.UnitTests.Engine;

public sealed class RuleAllowancesTests
{
    private const string Prelude = """
        namespace System
        {
            public class Object { }
            public class ValueType { }
            public struct Void { }
            public struct Boolean { }
            public class Attribute { }
        }
        public sealed class FactAttribute : System.Attribute { }
        """;

    [Fact]
    public async Task A_trailing_allowance_allows_findings_that_start_on_its_line()
    {
        var workspace = Workspace(
            """
            class C
            {
                static void Hit() { }

                void M()
                {
                    Hit(); // drillpress-allow HIT: the legacy importer needs it
                    Hit();
                }
            }
            """
        );

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            HIT Do not call Hit.
            Calls.cs
              8:9

            """,
            result.Output
        );
    }

    [Fact]
    public async Task A_standalone_allowance_allows_the_whole_statement_or_declaration_below_it()
    {
        var workspace = Workspace(
            """
            class C
            {
                static void Hit() { }

                void Statement()
                {
                    // drillpress-allow HIT: the branch replays the legacy order
                    // A further comment line keeps the allowance attached.
                    if (true)
                    {
                        Hit();
                    }
                    Hit();
                }

                // drillpress-allow HIT: this test exercises Hit
                /// <summary>Documented.</summary>
                [Fact]
                void Declaration()
                {
                    Hit();
                    Hit();
                }

                [Fact]
                // drillpress-allow HIT: attributes belong to the declaration
                void AfterAttributes() => Hit();

                void Adjacent()
                {
                    // drillpress-allow HIT: only the first statement is allowed
                    Hit();Hit();
                }
            }
            """
        );

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            HIT Do not call Hit.
            Calls.cs
              13:9
              32:15

            """,
            result.Output
        );
    }

    [Fact]
    public async Task An_allowance_above_code_that_starts_no_statement_or_declaration_allows_that_line()
    {
        var workspace = Workspace(
            """
            class C
            {
                static bool Hit() => true;

                bool M() =>
                    // drillpress-allow HIT: the condition must read the legacy flag
                    Hit()
                    && Hit();
            }
            """
        );

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            HIT Do not call Hit.
            Calls.cs
              8:12

            """,
            result.Output
        );
    }

    [Fact]
    public async Task Allowed_findings_offer_no_fix_and_other_findings_keep_theirs()
    {
        var workspace = Workspace(
            """
            class C
            {
                static void Hit() { }
                static void Run() { }

                void M()
                {
                    Hit(); // drillpress-allow HIT: the legacy importer needs it
                    Hit();
                }
            }
            """
        );
        var rules = new RuleCatalog();
        rules
            .Rule("HIT", "Call Run instead of Hit.")
            .For(Calls("Hit"))
            .Forbid(fix: call =>
                SourceChanges.Propose(
                    [SourceChanges.Replace(call.Source, call.Operation.Syntax.Span, "Run()")],
                    _ => true
                )
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            HIT Call Run instead of Hit.
            Calls.cs
              +9:9

            """,
            result.Output
        );
        Assert.Equal(
            """
            class C
            {
                static void Hit() { }
                static void Run() { }

                void M()
                {
                    Hit(); // drillpress-allow HIT: the legacy importer needs it
                    Run();
                }
            }
            """,
            result.FixedText("Calls.cs")
        );
    }

    [Fact]
    public async Task Unused_allowances_are_reported_with_a_fix_that_removes_them()
    {
        var workspace = Workspace(
            """
            class C
            {
                static void Hit() { }
                static void Other() { }

                void M()
                {
                    // drillpress-allow HIT: nothing below calls Hit
                    Other();
                    Other(); // drillpress-allow HIT: nothing here calls Hit
                }
            }
            """
        );

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            drillpress-allow-unused Remove the listed rules from this allowance; they report nothing in its scope.
            Calls.cs
              +8:9 [HIT]
              +10:18 [HIT]

            """,
            result.Output
        );
        Assert.Equal(
            """
            class C
            {
                static void Hit() { }
                static void Other() { }

                void M()
                {
                    Other();
                    Other();
                }
            }
            """,
            result.FixedText("Calls.cs")
        );
    }

    [Fact]
    public async Task Unused_allowances_over_conditional_code_are_reported_without_a_fix()
    {
        const string source = """
            class C
            {
                static void Hit() { }

                // drillpress-allow HIT: the release build replays the legacy order
                void M()
                {
            #if RELEASE
                    Hit();
            #endif
                }
            }
            """;
        var workspace = Workspace(source);

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            drillpress-allow-unused Remove the listed rules from this allowance; they report nothing in its scope.
            Calls.cs
              5:5 [HIT; another build configuration may use it]

            """,
            result.Output
        );
        Assert.Equal(source, result.FixedText("Calls.cs"));
    }

    [Fact]
    public async Task Rules_that_an_allowance_names_without_need_are_removed_from_it()
    {
        var workspace = Workspace(
            """
            class C
            {
                static void Hit() { }

                void M()
                {
                    Hit(); // drillpress-allow RUN,HIT , BOTH: the legacy importer needs it
                }
            }
            """
        );

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            drillpress-allow-unused Remove the listed rules from this allowance; they report nothing in its scope.
            Calls.cs
              +7:16 [RUN, BOTH]

            """,
            result.Output
        );
        Assert.Equal(
            """
            class C
            {
                static void Hit() { }

                void M()
                {
                    Hit(); // drillpress-allow HIT: the legacy importer needs it
                }
            }
            """,
            result.FixedText("Calls.cs")
        );
    }

    [Fact]
    public async Task The_narrowest_allowance_allows_a_finding_so_a_redundant_outer_one_is_unused()
    {
        var workspace = Workspace(
            """
            class C
            {
                static void Hit() { }

                // drillpress-allow HIT: the method replays legacy calls
                void M()
                {
                    Hit(); // drillpress-allow HIT: the legacy importer needs it
                }
            }
            """
        );

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            drillpress-allow-unused Remove the listed rules from this allowance; they report nothing in its scope.
            Calls.cs
              +5:5 [HIT]

            """,
            result.Output
        );
    }

    [Fact]
    public async Task Invalid_allowances_are_reported_and_allow_nothing()
    {
        var workspace = Workspace(
            """
            class C
            {
                static void Hit() { }

                void M()
                {
                    Hit(); // drillpress-allow HIT
                    Hit(); // drillpress-allow HTI, RUN: the legacy importer needs it
                    Hit(); // drillpress-allow : no rule
                    Hit(); // drillpress-allow HIT,: an empty entry
                    Hit(); // drillpress-allowed HIT: not an allowance
                    // drillpress-allow HIT: a blank line separates it from the code

                    Hit();
                }
            }
            """
        );

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            HIT Do not call Hit.
            Calls.cs
              7:9
              8:9
              9:9
              10:9
              11:9
              14:9
            drillpress-allow-invalid Write allowances as // drillpress-allow RULE-ID[, RULE-ID]: reason, after or directly above the code.
            Calls.cs
              7:16 [missing reason]
              8:16 [unknown rule HTI]
              9:16 [missing rule ID]
              10:16 [missing rule ID]
              12:9 [no code directly below]

            """,
            result.Output
        );
    }

    [Fact]
    public async Task A_conditional_directive_between_an_allowance_and_code_makes_the_allowance_invalid()
    {
        var source = new TestSource(
            "Calls.cs",
            """
            class C
            {
                static void Hit() { }

                void M()
                {
                    // drillpress-allow HIT: only the trace build calls it
            #if TRACE_ON
                    Hit();
            #endif
                    Hit();
                }
            }
            """
        );
        var workspace = new RuleTestWorkspace([]);
        workspace.AddProject(
            "Product",
            [new("System.cs", Prelude), source],
            framework: "net9.0",
            symbols: ["TRACE_ON"]
        );
        workspace.AddProject("Product", [new("System.cs", Prelude), source], framework: "net10.0");

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            HIT Do not call Hit.
            Calls.cs
              9:9
              11:9
            drillpress-allow-invalid Write allowances as // drillpress-allow RULE-ID[, RULE-ID]: reason, after or directly above the code.
            Calls.cs
              7:9 [no code directly below]

            """,
            result.Output
        );
    }

    [Fact]
    public async Task Built_in_rules_have_fix_complexity_and_cannot_be_allowed()
    {
        var workspace = new RuleTestWorkspace([]);
        var project = workspace.AddProject(
            "Product",
            [
                new("System.cs", Prelude),
                new(
                    "Calls.cs",
                    """
                    class C
                    {
                        void M()
                        {
                        } // drillpress-allow drillpress-allow-unused: built-in rules are not allowable
                    }
                    """
                ),
            ]
        );

        var output = await CheckAsync(
            Rules(),
            new AnalysisOptions(),
            project.Snapshot,
            project,
            showFixComplexity: true
        );

        Assert.Equal(
            """
            drillpress-allow-invalid [fix:local] Write allowances as // drillpress-allow RULE-ID[, RULE-ID]: reason, after or directly above the code.
            Calls.cs
              5:7 [unknown rule drillpress-allow-unused]

            """,
            output
        );
    }

    [Fact]
    public async Task An_allowance_is_used_when_any_context_of_a_linked_file_reports_its_finding()
    {
        var source = new TestSource(
            "Shared.cs",
            """
            class Shared
            {
                static void Hit() { }

                void M()
                {
                    // drillpress-allow HIT: only the legacy build calls it
                    {
            #if LEGACY
                        Hit();
            #endif
                    }
                }
            }
            """
        );
        var workspace = new RuleTestWorkspace([]);
        workspace.AddProject(
            "Product",
            [new("System.cs", Prelude), source],
            framework: "net9.0",
            symbols: ["LEGACY"]
        );
        workspace.AddProject("Product", [new("System.cs", Prelude), source], framework: "net10.0");

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.Equal("", result.Output);
    }

    [Fact]
    public async Task Allowances_for_skipped_coverage_rules_are_neither_used_nor_reported()
    {
        var workspace = new RuleTestWorkspace([]);
        var project = workspace.AddProject(
            "Product",
            [
                new("System.cs", Prelude),
                new(
                    "Calls.cs",
                    """
                    class C
                    {
                        static void Hit() { }

                        void M() => Hit(); // drillpress-allow COVERED: exercised by an end-to-end test
                    }
                    """
                ),
            ]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("COVERED", "Exercise each Hit call.")
            .For(Calls("Hit"))
            .Require(global::DrillPress.Coverage.Executed);

        var output = await CheckAsync(
            rules,
            new AnalysisOptions { SkipCoverage = true },
            project.Snapshot,
            project
        );

        Assert.Equal("skipped coverage rules: COVERED\n", output);
    }

    [Fact]
    public async Task Allowances_outside_the_analysis_scope_are_ignored()
    {
        var workspace = new RuleTestWorkspace([]);
        var project = workspace.AddProject(
            "Dependency",
            [new("System.cs", Prelude), new("Calls.cs", "class C { } // drillpress-allow HTI\n")]
        );

        var output = await CheckAsync(
            Rules(),
            new AnalysisOptions(),
            project.Snapshot with
            {
                IsAnalysisTarget = false,
            },
            project
        );

        Assert.Equal("", output);
    }

    [Fact]
    public async Task Generated_documents_contain_no_allowances()
    {
        var workspace = new RuleTestWorkspace([]);
        workspace.AddProject(
            "Product",
            [
                new("System.cs", Prelude),
                new("Calls.g.cs", "class C { } // drillpress-allow HTI\n", Generated: true),
            ]
        );

        var result = await workspace.CheckAsync(Rules(), TestContext.Current.CancellationToken);

        Assert.Equal("", result.Output);
    }

    private static RuleTestWorkspace Workspace(string source)
    {
        var workspace = new RuleTestWorkspace([]);
        workspace.AddProject("Product", [new("System.cs", Prelude), new("Calls.cs", source)]);
        return workspace;
    }

    private static RuleCatalog Rules()
    {
        var rules = new RuleCatalog();
        rules.Rule("HIT", "Do not call Hit.").For(Calls("Hit")).Forbid();
        rules.Rule("RUN", "Do not call Run.").For(Calls("Run")).Forbid();
        rules.Rule("BOTH", "Do not call Both.").For(Calls("Both")).Forbid();
        return rules;
    }

    private static CodeQuery<CodeInvocation> Calls(string name) =>
        Code.Calls.Where(call => call.Target.Name == name);

    private static async Task<string> CheckAsync(
        RuleCatalog rules,
        AnalysisOptions options,
        ProjectSnapshot snapshot,
        AnalysisProject project,
        bool showFixComplexity = false
    )
    {
        var request = CompilationSnapshot.Create(snapshot);
        var response = await new AnalysisEngine().EvaluateAsync(
            rules,
            request.RequestId,
            [new CompilationContext(snapshot, project.Compilation)],
            options,
            TestContext.Current.CancellationToken
        );
        return new CompactDiagnosticRenderer().Render(
            new BundleResponseValidator().Validate(request, response),
            showFixComplexity
        );
    }
}
