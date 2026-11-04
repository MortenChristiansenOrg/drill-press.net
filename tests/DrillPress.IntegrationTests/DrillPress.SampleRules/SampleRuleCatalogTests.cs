using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace DrillPress.IntegrationTests.SampleRules;

public sealed class SampleRuleCatalogTests(SemanticRuleFixture fixture)
    : IClassFixture<SemanticRuleFixture>
{
    [Fact]
    public async Task A_rename_only_local_is_inlined_into_its_single_read()
    {
        var project = fixture.Project(
            """
            class C
            {
                static int Compute() => 1;

                int M()
                {
                    var value = Compute();
                    return value;
                }
            }
            """
        );

        var findings = await Describe(project);

        Assert.Equal(
            [
                (
                    "DP1006",
                    7,
                    "var value = Compute()",
                    (string?)
                        """
                        class C
                        {
                            static int Compute() => 1;

                            int M()
                            {
                                return Compute();
                            }
                        }
                        """
                ),
            ],
            findings
        );
    }

    [Fact]
    public async Task Locals_read_more_than_once_written_or_in_test_projects_are_not_reported()
    {
        const string source = """
            class C
            {
                static int Compute() => 1;

                int M()
                {
                    var twice = Compute();
                    var written = Compute();
                    written = twice;
                    return twice + written;
                }
            }
            """;
        var tests = fixture.Project(
            "class D { int M() { var value = 1; return value; } }",
            name: "Tests",
            isTest: true
        );
        var library = fixture.Project(source);

        var findings = await Describe(tests, library);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task A_local_that_converts_its_initializer_keeps_the_finding_without_a_fix()
    {
        var project = fixture.Project(
            """
            class C
            {
                int M()
                {
                    object value = 1;
                    return value.GetHashCode();
                }
            }
            """
        );

        var findings = await Describe(project);

        Assert.Equal([("DP1006", 5, "object value = 1", (string?)null)], findings);
    }

    // Each finding with the source after its own validated batch, or null when its fix was withheld.
    private async Task<(string Rule, int Line, string Text, string? Fixed)[]> Describe(
        params AnalysisProject[] projects
    )
    {
        var response = await fixture.Evaluate(projects);
        return response
            .Contexts.SelectMany(context =>
                context.Findings.Select(finding =>
                {
                    var source = projects
                        .Single(project => project.Snapshot.ContextId == context.ContextId)
                        .Sources.Single(source => source.Document.DocumentId == finding.DocumentId);
                    var batch = response.Batches.SingleOrDefault(batch =>
                        batch.Id == finding.BatchId
                    );
                    return (
                        finding.RuleId,
                        source.Locate(new(finding.Start, finding.Length)).Line,
                        source.Document.Text.Substring(finding.Start, finding.Length),
                        batch is null
                            ? null
                            : SourceText
                                .From(source.Document.Text)
                                .WithChanges(
                                    batch.Edits.Select(edit => new TextChange(
                                        new(edit.Start, edit.Length),
                                        edit.Replacement
                                    ))
                                )
                                .ToString()
                    );
                })
            )
            .ToArray();
    }
}
