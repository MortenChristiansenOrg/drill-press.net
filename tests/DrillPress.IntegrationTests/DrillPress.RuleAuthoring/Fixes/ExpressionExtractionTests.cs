using DrillPress;
using DrillPress.Collections;
using DrillPress.Configuration;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Queries;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class ExpressionExtractionTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public async Task Constant_extraction_inserts_one_private_member_and_replaces_only_selected_occurrences()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { static void Use(string value) {} void M() { Use(\"/x\"); Use(\"/x\"); } string N() => \"/x\";}"
                ),
            ]
        );
        var rules = Constants();

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([true], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(
            """
            class A { static void Use(string value) {} void M() { Use(global::A.Route); Use(global::A.Route); } string N() => "/x";
                private const string Route = "/x";
            }
            """.ReplaceLineEndings("\n"),
            result.FixedText("A.cs")
        );
    }

    [Fact]
    public async Task Equivalent_existing_constants_are_reused_with_bound_qualification()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { private const string Existing = \"/x\"; static void Use(string value) {} void M() { string Existing = \"shadow\"; Use(\"/x\"); Use(\"/x\"); } }"
                ),
            ]
        );

        var result = await workspace.CheckAsync(Constants(), TestContext.Current.CancellationToken);

        Assert.Equal([true], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(
            "class A { private const string Existing = \"/x\"; static void Use(string value) {} void M() { string Existing = \"shadow\"; Use(global::A.Existing); Use(global::A.Existing); } }",
            result.FixedText("A.cs")
        );
    }

    [Fact]
    public async Task One_hole_helpers_retain_each_capture_and_an_allowlisted_encoder()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { static void Use(string value) {} static string Encode(string value) => value; void M(string first, string second) { Use($\"/x/{Encode(first)}\"); Use($\"/x/{Encode(second)}\"); }}"
                ),
            ]
        );
        var rules = new RuleSet();
        rules
            .For(
                ExpressionGroups.OneHoleTemplates(
                    Selected(),
                    new(
                        TemplateShapes.Interpolation,
                        capture => capture.TypeIs(CodeType.Of<string>()),
                        new ApiSet(CodeType.Named("A").Member("Encode"))
                    )
                )
            )
            .Forbid(
                "EXTRACT",
                "Extract the configured template.",
                fix: group =>
                    Fix.Extract(group)
                        .ToMethod("Route")
                        .Propose(evidence =>
                            evidence.Occurrences.All(change => change.Inputs.Count == 1)
                                ? ProofResult.Proven
                                : ProofResult.Unknown
                        )
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([true], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(
            """
            class A { static void Use(string value) {} static string Encode(string value) => value; void M(string first, string second) { Use(global::A.Route(first)); Use(global::A.Route(second)); }
                private static string Route(string value) => $"/x/{Encode(value)}";
            }
            """.ReplaceLineEndings("\n"),
            result.FixedText("A.cs")
        );
    }

    [Theory]
    [InlineData(
        "class A { string Route = \"taken\"; static void Use(string value) {} void M() { Use(\"/x\"); Use(\"/x\"); } }"
    )]
    [InlineData(
        "class A { const string First = \"/x\"; const string Second = \"/x\"; static void Use(string value) {} void M() { Use(\"/x\"); Use(\"/x\"); } }"
    )]
    [InlineData(
        "class A { static void Use(string value) {} void M() { Use(\"/\" /*keep*/ + \"x\"); Use(\"/x\"); } }"
    )]
    [InlineData(
        "class A { static string Use(string value) => value; System.Linq.Expressions.Expression<System.Func<string>> M() => () => Use(\"/x\"); string N() => Use(\"/x\"); }"
    )]
    [InlineData(
        "class A { static void Use(string value, [System.Runtime.CompilerServices.CallerArgumentExpression(\"value\")] string expression = \"\") {} void M() { Use(\"/x\"); Use(\"/x\"); } }"
    )]
    [InlineData(
        "class A { static void Use(string value) {} void M() { checked { Use(\"/x\"); Use(\"/x\"); } } }"
    )]
    [InlineData("class A { static void Use(string? value) {} void M() { Use(null); Use(null); } }")]
    [InlineData(
        "class A { static void Use(string value) {} void M() { Use(\"/x\"); Use(\"/x\"); } }\nclass B { static void Log([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) {} void N() => Log(); }"
    )]
    public async Task Unsupported_or_observable_cases_keep_the_diagnostic(string source)
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", source)]);

        var result = await workspace.CheckAsync(Constants(), TestContext.Current.CancellationToken);

        Assert.Equal([false], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(source, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Unknown_behavior_never_authorizes_extraction()
    {
        var workspace = fixture.Workspace();
        const string source =
            "class A { static void Use(string value) {} void M() { Use(\"/x\"); Use(\"/x\"); } }";
        workspace.AddProject("Library", [new("A.cs", source)]);

        var result = await workspace.CheckAsync(
            Constants(ProofResult.Unknown),
            TestContext.Current.CancellationToken
        );

        Assert.Equal([false], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(source, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Explicit_partial_destination_gets_one_member_and_all_selected_files_change()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "partial class A { static void Use(string value) {} void M() { Use(\"/x\"); } }"
                ),
                new("B.cs", "partial class A { void N() { Use(\"/x\"); }}"),
            ]
        );
        var query = ExpressionGroups.Constants(Selected());
        var rules = new RuleSet();
        rules
            .For(query)
            .Forbid(
                "EXTRACT",
                "Extract.",
                fix: group =>
                {
                    var destination = group
                        .Occurrences.Single(occurrence =>
                            occurrence.Expression.Source.Document.Path == "B.cs"
                        )
                        .Expression.Source;
                    var part = destination
                        .Tree.GetRoot()
                        .DescendantNodes()
                        .OfType<TypeDeclarationSyntax>()
                        .Single();
                    return Fix.Extract(group)
                        .ToConstant("Route")
                        .InPart(new(destination, part))
                        .Propose(_ => ProofResult.Proven);
                }
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([true], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(
            "partial class A { static void Use(string value) {} void M() { Use(global::A.Route); } }",
            result.FixedText("A.cs")
        );
        Assert.Equal(
            """
            partial class A { void N() { Use(global::A.Route); }
                private const string Route = "/x";
            }
            """.ReplaceLineEndings("\n"),
            result.FixedText("B.cs")
        );
    }

    [Fact]
    public async Task Suffix_allocation_is_explicit_and_considers_existing_members()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { int Route; int Route1; static void Use(string value) {} void M() { Use(\"/x\"); Use(\"/x\"); }}"
                ),
            ]
        );
        var rules = new RuleSet();
        rules
            .For(ExpressionGroups.Constants(Selected()))
            .Forbid(
                "EXTRACT",
                "Extract.",
                fix: group =>
                    Fix.Extract(group)
                        .ToConstant("Route", collision: ExtractionNameCollision.AddNumericSuffix)
                        .Propose(_ => ProofResult.Proven)
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([true], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(
            """
            class A { int Route; int Route1; static void Use(string value) {} void M() { Use(global::A.Route2); Use(global::A.Route2); }
                private const string Route2 = "/x";
            }
            """.ReplaceLineEndings("\n"),
            result.FixedText("A.cs")
        );
    }

    [Theory]
    [InlineData(
        false,
        true,
        """
            class A { static void Use(string value) {} void M() { Use(global::A.Route); Use(global::A.Route); }
                private const string Route = "/x";
            }
            """
    )]
    [InlineData(
        true,
        false,
        "class A { static void Use(string value) {} void M() { Use(\"/x\"); Use(\"/x\"); }}"
    )]
    public async Task Linked_contexts_without_a_finding_still_run_the_behavior_proof(
        bool veto,
        bool expectedFix,
        string expectedText
    )
    {
        var workspace = fixture.Workspace();
        const string source =
            "class A { static void Use(string value) {} void M() { Use(\"/x\"); Use(\"/x\"); }}";
        workspace.AddProject("First", [new("A.cs", source)]);
        workspace.AddProject("Second", [new("A.cs", source)]);
        var rules = new RuleSet();
        rules
            .For(
                ExpressionGroups.Constants(
                    Selected()
                        .Where(expression => expression.Source.Project.Snapshot.Name == "First")
                )
            )
            .Forbid(
                "EXTRACT",
                "Extract.",
                fix: group =>
                    Fix.Extract(group)
                        .ToConstant("Route")
                        .Propose(evidence =>
                            veto && evidence.Context.Original.Snapshot.Name == "Second"
                                ? ProofResult.Unknown
                                : ProofResult.Proven
                        )
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([expectedFix], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(expectedText.ReplaceLineEndings("\n"), result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Contextual_constant_values_must_match_the_inserted_value()
    {
        var workspace = fixture.Workspace();
        const string source =
            "partial class A { static void Use(string value) {} void M() { Use(Value); Use(Value); }}";
        workspace.AddProject(
            "First",
            [
                new("A.cs", source),
                new("First.cs", "partial class A { const string Value = \"first\"; }"),
            ]
        );
        workspace.AddProject(
            "Second",
            [
                new("A.cs", source),
                new("Second.cs", "partial class A { const string Value = \"second\"; }"),
            ]
        );
        var rules = new RuleSet();
        rules
            .For(
                ExpressionGroups.Constants(
                    Selected()
                        .Where(expression => expression.Source.Project.Snapshot.Name == "First")
                )
            )
            .Forbid(
                "EXTRACT",
                "Extract.",
                fix: group =>
                    Fix.Extract(group)
                        .ToConstant("Route", reuseExisting: false)
                        .Propose(_ => ProofResult.Proven)
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([false], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(source, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Adding_a_member_must_not_change_lookup_in_an_unselected_partial_file()
    {
        var workspace = fixture.Workspace();
        const string first =
            "partial class A { static void Use(string value) {} void M() { Use(\"/x\"); Use(\"/x\"); }}";
        const string second =
            "using static Other; partial class A { string N() => Route; } static class Other { public const string Route = \"other\"; }";
        workspace.AddProject("Library", [new("A.cs", first), new("B.cs", second)]);

        var result = await workspace.CheckAsync(Constants(), TestContext.Current.CancellationToken);

        Assert.Equal([false], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(first, result.FixedText("A.cs"));
        Assert.Equal(second, result.FixedText("B.cs"));
    }

    [Fact]
    public async Task Independent_extractions_cannot_reserve_the_same_member_name()
    {
        var workspace = fixture.Workspace();
        const string source =
            "class A { static void Use(string value) {} void M() { Use(\"/x\"); Use(\"/x\"); Use(\"/y\"); Use(\"/y\"); }}";
        workspace.AddProject("Library", [new("A.cs", source)]);

        var result = await workspace.CheckAsync(Constants(), TestContext.Current.CancellationToken);

        Assert.Equal([false, false], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(source, result.FixedText("A.cs"));
    }

    [Theory]
    [InlineData("\"/x/\" + first", "\"/x/\" + second", "\"/x/\" + value", "string")]
    [InlineData("$\"/x/{first,3:X}\"", "$\"/x/{second,3:X}\"", "$\"/x/{value, 3:X}\"", "int")]
    public async Task Helpers_preserve_supported_concatenation_and_formatting(
        string first,
        string second,
        string body,
        string type
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    $"class A {{ static void Use(string value) {{}} void M({type} first, {type} second) {{ Use({first}); Use({second}); }}}}"
                ),
            ]
        );
        var rules = new RuleSet();
        rules
            .For(
                ExpressionGroups.OneHoleTemplates(
                    Selected(),
                    new(TemplateShapes.Interpolation | TemplateShapes.Concatenation, _ => true)
                )
            )
            .Forbid(
                "EXTRACT",
                "Extract.",
                fix: group => Fix.Extract(group).ToMethod("Route").Propose(_ => ProofResult.Proven)
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([true], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(
            $$"""
            class A { static void Use(string value) {} void M({{type}} first, {{type}} second) { Use(global::A.Route(first)); Use(global::A.Route(second)); }
                private static string Route({{type}} value) => {{body}};
            }
            """.ReplaceLineEndings("\n"),
            result.FixedText("A.cs")
        );
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task Member_insertion_preserves_the_input_newline_convention(string newline)
    {
        var workspace = fixture.Workspace();
        var source = """
            class A
            {
                static void Use(string value) {}
                void M() { Use("/x"); Use("/x"); }
            }
            """.ReplaceLineEndings(newline);
        workspace.AddProject("Library", [new("A.cs", source)]);

        var result = await workspace.CheckAsync(Constants(), TestContext.Current.CancellationToken);

        Assert.Equal([true], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(
            """
            class A
            {
                static void Use(string value) {}
                void M() { Use(global::A.Route); Use(global::A.Route); }
                private const string Route = "/x";
            }
            """.ReplaceLineEndings(newline),
            result.FixedText("A.cs")
        );
    }

    private static CodeQuery<CodeExpression> Selected() =>
        Sources
            .Nodes<ArgumentSyntax>()
            .Where(node =>
                node.Syntax.Parent?.Parent
                    is InvocationExpressionSyntax
                    {
                        Expression: IdentifierNameSyntax { Identifier.ValueText: "Use" }
                    }
            )
            .Select(node => new CodeExpression(node.Source, node.Syntax.Expression));

    private static RuleSet Constants(ProofResult behavior = ProofResult.Proven)
    {
        var rules = new RuleSet();
        rules
            .For(ExpressionGroups.Constants(Selected()))
            .Forbid(
                "EXTRACT",
                "Extract the constant.",
                fix: group => Fix.Extract(group).ToConstant("Route").Propose(_ => behavior)
            );
        return rules;
    }
}
