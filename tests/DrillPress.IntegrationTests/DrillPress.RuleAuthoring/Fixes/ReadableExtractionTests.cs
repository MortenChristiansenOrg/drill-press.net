using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class ReadableExtractionTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Theory]
    [InlineData("first", "second", "value")]
    [InlineData("id", "id", "id")]
    [InlineData("x.ServiceProviderId", "y.ServiceProviderId", "serviceProviderId")]
    [InlineData("@class", "@class", "value")]
    public async Task Capture_names_require_agreement(string first, string second, string parameter)
    {
        var workspace = fixture.Workspace();
        const string prefix =
            "class Item { public string ServiceProviderId => \"id\"; } class A { static void Use(string value) {} void M(string first, string second, string id, string @class, Item x, Item y) { ";
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    prefix + $"Use($\"/api/{{{first}}}\"); Use($\"/api/{{{second}}}\"); }} }}"
                ),
            ]
        );
        var urls = Code
            .Calls.To(CodeType.Named("A").Member("Use"))
            .ArgumentsFor("value")
            .SourceValues();
        var groups = urls.TemplateGroups(
            new(TemplateShapes.Interpolation, capture => capture.TypeIs<string>())
        );
        var rules = new RuleSet();
        rules
            .Rule("URL", "Extract URL.")
            .For(groups)
            .Forbid(fix: group =>
                Fix.Extract(group)
                    .ToMethod("CreateUrl", ParameterName.FromCapture)
                    .SafeWhen(_ => true)
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([true], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(
            $$"""
            {{prefix}}Use(global::A.CreateUrl({{first}})); Use(global::A.CreateUrl({{second}})); }{{' '}}
                private static string CreateUrl(string {{parameter}}) => $"/api/{{{parameter}}}";
            }
            """.ReplaceLineEndings("\n"),
            result.FixedText("A.cs")
        );
    }

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
            .Calls.To(CodeType.Named("A").Member("Use"))
            .ArgumentsFor("value")
            .SourceValues();
        var groups = urls.TemplateGroups(
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
            .Rule("URL", "Extract URL.")
            .For(groups)
            .Forbid(fix: group =>
                Fix.Extract(group)
                    .OnlyWhenGroupCoversAll(urls)
                    .ToMethod("CreateUrl", ParameterName.FromCapture)
                    .SafeWhen(change =>
                        change.Occurrences.All(occurrence => occurrence.Inputs.Count == 1)
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
            .Calls.To(CodeType.Named("A").Member("Use"))
            .ArgumentsFor("value")
            .SourceValues();
        var literals = urls.Where(value =>
            value.Syntax is Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax
        );
        var rules = new RuleSet();
        rules
            .Rule("URL", "Extract URL.")
            .For(literals.ConstantGroups())
            .Forbid(fix: group =>
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
            .Calls.To(CodeType.Named("A").Member("Use"))
            .ArgumentsFor("value")
            .SourceValues();
        var groups = urls.ConstantGroups();
        var rules = new RuleSet();
        rules
            .Rule("URL", "Extract URL.")
            .For(
                urls.WithGroupsFrom(groups).Where(membership => membership.UniqueGroup is not null)
            )
            .Forbid(fix: membership =>
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
            .Calls.To(CodeType.Named("A").Member("Use"))
            .ArgumentsFor("value")
            .SourceValues();
        var rules = new RuleSet();
        rules
            .Rule("URL", "Extract URL.")
            .For(urls.ConstantGroups())
            .Forbid(fix: group =>
                Fix.Extract(group).OnlyWhenGroupCoversAll(urls).ToConstant("_url").Propose()
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([false], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(source.Text, result.FixedText("A.cs"));
    }
}
