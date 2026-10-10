using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class ExtractionEvidenceTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task Semantic_occurrences_use_actual_memberships_including_linked_contexts_without_findings(
        string newline
    )
    {
        var workspace = fixture.Workspace();
        var source = new TestSource(
            "A.cs",
            """
            class A
            {
                string First(string first) => $"/api/{first}";
                string Second(string second) => $"/api/{second}";
            }
            """.ReplaceLineEndings(newline)
        );
        workspace.AddProject("Primary", [source]);
        workspace.AddProject("Linked", [source]);
        var observed = new List<OccurrenceEvidence>();
        var rules = new RuleCatalog();
        rules
            .Rule("EXTRACT", "Extract.")
            .For(
                Code.Nodes<InterpolatedStringExpressionSyntax>()
                    .InProject("Primary")
                    .Expressions()
                    .TemplateGroups(
                        new(TemplateShapes.Interpolation, capture => capture.TypeIs<string>())
                    )
            )
            .Forbid(fix: group =>
                Fix.Extract(group)
                    .ToMethod("FormatValue")
                    .SafeWhen(change =>
                    {
                        observed.AddRange(
                            change.Occurrences.Select(occurrence => new OccurrenceEvidence(
                                change.Context.Original.Snapshot.Name,
                                occurrence.Before.Type!.ToDisplayString(),
                                occurrence.After.Type!.ToDisplayString(),
                                occurrence.Kept.Single().Before.Symbol!.Name,
                                occurrence.Kept.Single().After.Single().Symbol!.Name,
                                occurrence.Before.Source.Project == change.Context.Original
                                    && occurrence.Before.Syntax.SyntaxTree
                                        == occurrence.Before.Source.Tree
                                    && occurrence.Before.Source == occurrence.Rewrite.Source,
                                occurrence.After.Source.Project.Compilation
                                    == change.Context.Rewritten
                                    && occurrence.After.Syntax.SyntaxTree
                                        == occurrence.After.Source.Tree
                                    && occurrence.After.Source.Model.Compilation
                                        == change.Context.Rewritten
                                    && occurrence.Kept.Single().After.Single().Source
                                        == occurrence.After.Source,
                                SymbolEqualityComparer.Default.Equals(
                                    occurrence.After.Symbol,
                                    change.Member
                                ),
                                occurrence.Rewrite.Inputs.Count
                            ))
                        );
                        return change.Occurrences.All(occurrence =>
                            occurrence.Before.TypeIs<string>()
                            && occurrence.After.TypeIs<string>()
                            && occurrence.Kept is [var capture]
                            && capture.Before.TypeIs<string>()
                            && capture.After is [var argument]
                            && argument.TypeIs<string>()
                        );
                    })
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new OccurrenceEvidence(
                    "Linked",
                    "string",
                    "string",
                    "first",
                    "first",
                    true,
                    true,
                    true,
                    1
                ),
                new OccurrenceEvidence(
                    "Linked",
                    "string",
                    "string",
                    "second",
                    "second",
                    true,
                    true,
                    true,
                    1
                ),
                new OccurrenceEvidence(
                    "Primary",
                    "string",
                    "string",
                    "first",
                    "first",
                    true,
                    true,
                    true,
                    1
                ),
                new OccurrenceEvidence(
                    "Primary",
                    "string",
                    "string",
                    "second",
                    "second",
                    true,
                    true,
                    true,
                    1
                ),
            ],
            observed
                .Distinct()
                .OrderBy(evidence => evidence.Context)
                .ThenBy(evidence => evidence.Capture)
        );
        Assert.Equal(
            """
            EXTRACT Extract.
            A.cs
              +3:35

            """.ReplaceLineEndings("\n"),
            result.Output
        );
        Assert.Equal(
            """
            class A
            {
                string First(string first) => global::A.FormatValue(first);
                string Second(string second) => global::A.FormatValue(second);
                private static string FormatValue(string value) => $"/api/{value}";
            }
            """.ReplaceLineEndings(newline),
            result.FixedText("A.cs")
        );
    }

    private sealed record OccurrenceEvidence(
        string Context,
        string BeforeType,
        string AfterType,
        string Capture,
        string Argument,
        bool OriginalMembership,
        bool RewrittenMembership,
        bool HelperBinding,
        int RawInputs
    );
}
