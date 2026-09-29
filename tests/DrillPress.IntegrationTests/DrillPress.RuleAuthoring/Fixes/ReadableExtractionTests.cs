using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class ReadableExtractionTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public async Task Member_capture_is_evaluated_at_the_call_site_and_wrapper_stays_in_helper()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class Item { public string ExternalId => \"id\"; } class Base { protected static string Encode(string value) => value; } class A : Base { static void Use(string value) {} void M(Item item) { Use($\"/api/{Encode(item.ExternalId)}\"); } }"
                ),
            ]
        );
        var urls = Code
            .Calls.Calling(CodeType.Named("A").Member("Use"))
            .ArgumentsFor("value")
            .SourceValues();
        var groups = ExpressionGroups.OneHoleTemplates(
            urls,
            new(
                TemplateShapes.Interpolation,
                capture => capture.TypeIs<string>(),
                allowingCalls: call =>
                    call.Target.IsStatic && call.Target.ContainingType.Name == "Base"
            ),
            minimumOccurrences: 1
        );
        var rules = new RuleSet();
        rules
            .For(groups)
            .Forbid(
                "URL",
                "Extract URL.",
                fix: group =>
                    Fix.Extract(group)
                        .OnlyWhenGroupCoversAll(urls)
                        .ToMethod("CreateUrl", ParameterName.FromCapture)
                        .SafeWhen(change =>
                            change.Occurrences.All(occurrence =>
                                occurrence.RetainedExpressions.Count == 1
                            )
                        )
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([true], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(
            """
            class Item { public string ExternalId => "id"; } class Base { protected static string Encode(string value) => value; } class A : Base { static void Use(string value) {} void M(Item item) { Use(global::A.CreateUrl(item.ExternalId)); } 
                private static string CreateUrl(string externalId) => $"/api/{Encode(externalId)}";
            }
            """.ReplaceLineEndings("\n"),
            result.FixedText("A.cs")
        );
    }

    [Fact]
    public async Task Coverage_includes_compliant_uses_in_other_partial_parts()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "partial class A { const string _url = \"/api\"; static void Use(string value) {} void M() { Use(\"/api\"); Use(\"/api\"); } }"
                ),
                new("B.cs", "partial class A { void N() { Use(_url); } }"),
            ]
        );
        var urls = Code
            .Calls.Calling(CodeType.Named("A").Member("Use"))
            .ArgumentsFor("value")
            .SourceValues();
        var literals = urls.Where(value =>
            value.Syntax is Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax
        );
        var rules = new RuleSet();
        rules
            .For(ExpressionGroups.Constants(literals))
            .Forbid(
                "URL",
                "Extract URL.",
                fix: group =>
                    Fix.Extract(group).OnlyWhenGroupCoversAll(urls).ToConstant("_url").Propose()
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([false], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(
            "partial class A { const string _url = \"/api\"; static void Use(string value) {} void M() { Use(\"/api\"); Use(\"/api\"); } }",
            result.FixedText("A.cs")
        );
        Assert.Equal("partial class A { void N() { Use(_url); } }", result.FixedText("B.cs"));
    }

    [Fact]
    public async Task Constant_default_proof_is_atomic_and_duplicate_proposals_are_deduplicated()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { static void Use(string value) {} void M() { Use(\"/api\"); Use(\"/api\"); } class Nested { void N() { Use(\"different\"); } } }"
                ),
            ]
        );
        var urls = Code
            .Calls.Calling(CodeType.Named("A").Member("Use"))
            .ArgumentsFor("value")
            .SourceValues();
        var groups = ExpressionGroups.Constants(urls);
        var rules = new RuleSet();
        rules
            .For(
                urls.WithGroupsFrom(groups).Where(membership => membership.UniqueGroup is not null)
            )
            .Forbid(
                "URL",
                "Extract URL.",
                fix: membership =>
                    Fix.Extract(membership.UniqueGroup!)
                        .OnlyWhenGroupCoversAll(urls)
                        .ToConstant("_url")
                        .Propose()
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([true, true], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(
            """
            class A { static void Use(string value) {} void M() { Use(global::A._url); Use(global::A._url); } class Nested { void N() { Use("different"); } } 
                private const string _url = "/api";
            }
            """.ReplaceLineEndings("\n"),
            result.FixedText("A.cs")
        );
    }

    [Fact]
    public async Task Coverage_is_rechecked_in_linked_contexts_without_the_same_findings()
    {
        var workspace = fixture.Workspace();
        var source = new TestSource(
            "A.cs",
            """
            class A { static void Use(string value) {} void M() {
                Use("/api"); Use("/api");
            #if EXTRA
                Use("different");
            #endif
            } }
            """
        );
        workspace.AddProject("Library", [source], framework: "net10.0");
        workspace.AddProject("Library", [source], framework: "net8.0", symbols: ["EXTRA"]);
        var urls = Code
            .Calls.Calling(CodeType.Named("A").Member("Use"))
            .ArgumentsFor("value")
            .SourceValues();
        var rules = new RuleSet();
        rules
            .For(ExpressionGroups.Constants(urls))
            .Forbid(
                "URL",
                "Extract URL.",
                fix: group =>
                    Fix.Extract(group).OnlyWhenGroupCoversAll(urls).ToConstant("_url").Propose()
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([false], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(source.Text, result.FixedText("A.cs"));
    }
}
