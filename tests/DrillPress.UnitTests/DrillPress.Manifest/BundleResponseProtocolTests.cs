using System.Text;
using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Manifest;

public sealed class BundleResponseProtocolTests
{
    [Fact]
    public void Clean_internal_golden_is_exact_UTF8_JSON()
    {
        var response = new BundleResponse(
            BundleResponseProtocol.CurrentVersion,
            "request",
            [new("context", true, [])],
            []
        );

        var output = BundleResponseProtocol.Serialize(response);

        Assert.Equal(
            Encoding.UTF8.GetBytes(
                $$"""{"protocolVersion":8,"requestId":"request","contexts":[{"contextId":"context","isComplete":true,"findings":[]}],"batches":[],"productVersion":"{{ComponentVersion.Current}}"}"""
            ),
            output
        );
    }

    [Fact]
    public void Fixable_linked_internal_golden_locks_complete_batches_and_nonreporting_validation()
    {
        var response = new BundleResponse(
            BundleResponseProtocol.CurrentVersion,
            "request",
            [
                new("first", true, [new("R", "Replace.", "doc", 2, 5, "fix")]),
                new("second", true, []),
            ],
            [
                new(
                    "fix",
                    [new("file", "hash", 2, 5, "alpha", "A")],
                    [new("first", true), new("second", false)]
                ),
            ]
        );

        var output = BundleResponseProtocol.Serialize(response);

        Assert.Equal(
            Encoding.UTF8.GetBytes(
                $$"""{"protocolVersion":8,"requestId":"request","contexts":[{"contextId":"first","isComplete":true,"findings":[{"ruleId":"R","message":"Replace.","documentId":"doc","start":2,"length":5,"batchId":"fix"}]},{"contextId":"second","isComplete":true,"findings":[]}],"batches":[{"id":"fix","edits":[{"fileIdentity":"file","fingerprint":"hash","start":2,"length":5,"originalText":"alpha","replacement":"A"}],"validations":[{"contextId":"first","isSafe":true},{"contextId":"second","isSafe":false}]}],"productVersion":"{{ComponentVersion.Current}}"}"""
            ),
            output
        );
    }

    [Fact]
    public void Nonfixable_single_context_internal_golden_is_exact()
    {
        var response = new BundleResponse(
            BundleResponseProtocol.CurrentVersion,
            "request",
            [new("context", true, [new("R", "Replace.", "doc", 2, 5, null)])],
            []
        );

        var output = BundleResponseProtocol.Serialize(response);

        Assert.Equal(
            Encoding.UTF8.GetBytes(
                $$"""{"protocolVersion":8,"requestId":"request","contexts":[{"contextId":"context","isComplete":true,"findings":[{"ruleId":"R","message":"Replace.","documentId":"doc","start":2,"length":5,"batchId":null}]}],"batches":[],"productVersion":"{{ComponentVersion.Current}}"}"""
            ),
            output
        );
    }

    [Theory]
    [InlineData(RuleFixComplexity.Trivial, "Trivial")]
    [InlineData(RuleFixComplexity.Local, "Local")]
    [InlineData(RuleFixComplexity.Complex, "Complex")]
    [InlineData(RuleFixComplexity.Architectural, "Architectural")]
    public void Assigned_complexity_roundtrips_through_generated_JSON(
        RuleFixComplexity complexity,
        string wireValue
    )
    {
        var fixture = new ContractFixture();
        var context = fixture.Response.Contexts[0];
        var finding = context.Findings[0] with { FixComplexity = complexity };
        var response = fixture.Response with
        {
            Contexts = [context with { Findings = [finding] }, fixture.Response.Contexts[1]],
        };

        var json = Encoding.UTF8.GetString(BundleResponseProtocol.Serialize(response));
        var result = BundleResponseProtocol.Read(json, fixture.Snapshot);

        Assert.Equal(
            $$"""{"protocolVersion":8,"requestId":"request","contexts":[{"contextId":"first","isComplete":true,"findings":[{"ruleId":"DP1004","message":"Replace alpha.","documentId":"document","start":2,"length":5,"batchId":"fix","fixComplexity":"{{wireValue}}"}]},{"contextId":"second","isComplete":true,"findings":[]}],"batches":[{"id":"fix","edits":[{"fileIdentity":"Shared.cs","fingerprint":"{{fixture.Document.Fingerprint}}","start":2,"length":5,"originalText":"alpha","replacement":"A"}],"validations":[{"contextId":"first","isSafe":true},{"contextId":"second","isSafe":true}]}],"productVersion":"{{ComponentVersion.Current}}"}""",
            json
        );
        Assert.Equal(
            new AggregatedFinding(
                "DP1004",
                "Replace alpha.",
                "Shared.cs",
                "Shared.cs",
                2,
                5,
                1,
                3,
                "fix"
            )
            {
                FixComplexity = complexity,
            },
            Assert.Single(result.Findings)
        );
    }

    [Fact]
    public void Selection_withholds_atomic_batches_shared_with_excluded_rules()
    {
        var fixture = new FixComplexityFixture();
        var response = fixture.SharedBatchResponse;
        var json = Encoding.UTF8.GetString(BundleResponseProtocol.Serialize(response));

        var result = BundleResponseProtocol.Read(
            json,
            fixture.Files.Snapshot,
            new HashSet<RuleFixComplexity?> { RuleFixComplexity.Trivial }
        );

        Assert.Equal(
            [
                new AggregatedFinding(
                    "R0",
                    "Replace alpha.",
                    fixture.Files.Documents[0].FileIdentity,
                    fixture.Files.Paths[0],
                    2,
                    5,
                    1,
                    3,
                    null
                )
                {
                    FixComplexity = RuleFixComplexity.Trivial,
                },
            ],
            result.Findings
        );
        Assert.Empty(result.Batches);
        Assert.Empty(result.Edits);
    }

    [Fact]
    public void Selection_does_not_hide_invalid_excluded_findings()
    {
        var fixture = new FixComplexityFixture();
        var context = fixture.Response.Contexts[0];
        var response = fixture.Response with
        {
            Contexts =
            [
                context with
                {
                    Findings = [context.Findings[1] with { Start = int.MaxValue }],
                },
            ],
        };
        var json = Encoding.UTF8.GetString(BundleResponseProtocol.Serialize(response));

        var error = Assert.Throws<InvalidDataException>(() =>
            BundleResponseProtocol.Read(
                json,
                fixture.Files.Snapshot,
                new HashSet<RuleFixComplexity?> { RuleFixComplexity.Trivial }
            )
        );

        Assert.Equal("Finding span is outside captured source.", error.Message);
    }

    [Fact]
    public void Selection_withholds_shared_edits_even_when_an_excluded_aggregated_finding_loses_its_batch()
    {
        var fixture = new ContractFixture();
        var selected = fixture.Response.Contexts[0].Findings[0] with
        {
            FixComplexity = RuleFixComplexity.Trivial,
        };
        var excluded = selected with
        {
            RuleId = "OTHER",
            FixComplexity = RuleFixComplexity.Complex,
        };
        var response = fixture.Response with
        {
            Contexts =
            [
                fixture.Response.Contexts[0] with
                {
                    Findings = [selected, excluded],
                },
                fixture.Response.Contexts[1] with
                {
                    Findings =
                    [
                        selected with
                        {
                            DocumentId = "linked",
                        },
                        excluded with
                        {
                            DocumentId = "linked",
                            BatchId = null,
                        },
                    ],
                },
            ],
        };
        var json = Encoding.UTF8.GetString(BundleResponseProtocol.Serialize(response));

        var result = BundleResponseProtocol.Read(
            json,
            fixture.Snapshot,
            new HashSet<RuleFixComplexity?> { RuleFixComplexity.Trivial }
        );

        Assert.Equal(
            new AggregatedFinding(
                "DP1004",
                "Replace alpha.",
                "Shared.cs",
                "Shared.cs",
                2,
                5,
                1,
                3,
                null
            )
            {
                FixComplexity = RuleFixComplexity.Trivial,
            },
            Assert.Single(result.Findings)
        );
        Assert.Empty(result.Edits);
        Assert.Empty(result.Batches);
    }
}
