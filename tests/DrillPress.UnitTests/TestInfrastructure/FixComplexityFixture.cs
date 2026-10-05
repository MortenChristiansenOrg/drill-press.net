using DrillPress.Manifest;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class FixComplexityFixture
{
    public FixFixture Files { get; } = new(fileCount: 3);
    public BundleResponse Response { get; }
    public BundleResponse SharedBatchResponse =>
        Response with
        {
            Contexts =
            [
                Response.Contexts[0] with
                {
                    Findings =
                    [
                        Response.Contexts[0].Findings[0],
                        Response.Contexts[0].Findings[1] with
                        {
                            BatchId = Response.Contexts[0].Findings[0].BatchId,
                        },
                    ],
                },
            ],
            Batches =
            [
                Response.Batches[0] with
                {
                    Edits = [.. Response.Batches[0].Edits, .. Response.Batches[1].Edits],
                },
            ],
        };
    public FixProcessRunner Runner { get; }

    public FixComplexityFixture()
    {
        RuleFixComplexity?[] complexities =
        [
            RuleFixComplexity.Trivial,
            RuleFixComplexity.Complex,
            null,
        ];
        Response = Files.Response with
        {
            Contexts = Files
                .Response.Contexts.Select(context =>
                    context with
                    {
                        Findings = context
                            .Findings.Select(
                                (finding, index) =>
                                    finding with
                                    {
                                        RuleId = $"R{index}",
                                        FixComplexity = complexities[index],
                                    }
                            )
                            .ToArray(),
                    }
                )
                .ToArray(),
        };
        Runner = new(Files) { Response = Response, RecheckResponse = Recheck };
    }

    private BundleResponse Recheck(CompilationSnapshot snapshot) =>
        Response with
        {
            Batches = [],
            Contexts = Response
                .Contexts.Select(context =>
                    context with
                    {
                        Findings = context
                            .Findings.Where(finding =>
                                snapshot
                                    .Projects.Single(project =>
                                        project.ContextId == context.ContextId
                                    )
                                    .Documents.Single(document =>
                                        document.DocumentId == finding.DocumentId
                                    )
                                    .Text.Contains("alpha")
                            )
                            .Select(finding => finding with { BatchId = null })
                            .ToArray(),
                    }
                )
                .ToArray(),
        };
}
