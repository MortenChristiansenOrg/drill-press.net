using System.IO.Abstractions.TestingHelpers;
using System.Text;
using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Manifest;

public sealed class CoverageCallEvidenceTests
{
    [Theory]
    [InlineData(false, false, false, ExecutionCoverage.Uncovered)]
    [InlineData(false, true, false, ExecutionCoverage.Uncovered)]
    [InlineData(true, true, false, ExecutionCoverage.Covered)]
    [InlineData(true, false, true, ExecutionCoverage.Covered)]
    [InlineData(true, false, false, ExecutionCoverage.Unknown)]
    public void Execution_requires_a_reached_call_or_an_unhit_entry(
        bool entry,
        bool safePrefix,
        bool completion,
        ExecutionCoverage expected
    )
    {
        var call = new CoverageCallEvidence(
            "document",
            "Shared.cs",
            0x6000001,
            8,
            3,
            entry,
            safePrefix,
            4,
            completion
        );

        var state = call.State;

        Assert.Equal(expected, state);
    }

    [Fact]
    public void Call_proofs_roundtrip_with_their_original_source_and_compiled_identity()
    {
        var fixture = new ContractFixture();
        var call = new CoverageCallEvidence(
            "document",
            "Shared.cs",
            0x6000001,
            8,
            3,
            true,
            false,
            4,
            true
        );
        var response = Response(fixture, call, ExecutionCoverage.Covered);

        var result = BundleResponseProtocol.Read(
            Encoding.UTF8.GetString(BundleResponseProtocol.Serialize(response)),
            fixture.Snapshot
        );

        Assert.Equal(
            call,
            Assert.Single(Assert.Single(Assert.Single(result.Findings).Coverage!).Calls!)
        );
    }

    [Theory]
    [InlineData("missing", "Shared.cs", 0x6000001, 8, 3, true, 4, true)]
    [InlineData("document", "Other.cs", 0x6000001, 8, 3, true, 4, true)]
    [InlineData("document", "Shared.cs", 0x6000000, 8, 3, true, 4, true)]
    [InlineData("document", "Shared.cs", 0x2000001, 8, 3, true, 4, true)]
    [InlineData("document", "Shared.cs", 0x6000001, -1, 3, true, 4, true)]
    [InlineData("document", "Shared.cs", 0x6000001, 8, -1, true, 4, true)]
    [InlineData("document", "Shared.cs", 0x6000001, 8, 3, false, 4, true)]
    [InlineData("document", "Shared.cs", 0x6000001, 8, 3, true, 3, true)]
    [InlineData("document", "Shared.cs", 0x6000001, 8, 3, true, null, true)]
    [InlineData("document", "Shared.cs", 0x6000001, 8, 3, true, 4, null)]
    public void Rejects_proofs_with_invalid_origin_or_inconsistent_block_identity(
        string document,
        string path,
        int token,
        int offset,
        int block,
        bool entry,
        int? completion,
        bool? completionHit
    )
    {
        var fixture = new ContractFixture();
        var call = new CoverageCallEvidence(
            document,
            path,
            token,
            offset,
            block,
            entry,
            true,
            completion,
            completionHit
        );
        var response = Response(fixture, call, ExecutionCoverage.Covered);

        var error = Assert.Throws<InvalidDataException>(() =>
            new BundleResponseValidator().Validate(fixture.Snapshot, response)
        );

        Assert.Equal("Invalid coverage call proof.", error.Message);
    }

    [Fact]
    public void Rejects_a_covered_claim_with_only_an_unhit_call_block()
    {
        var fixture = new ContractFixture();
        var call = new CoverageCallEvidence(
            "document",
            "Shared.cs",
            0x6000001,
            8,
            3,
            false,
            true,
            4,
            false
        );
        var response = Response(fixture, call, ExecutionCoverage.Covered);

        var error = Assert.Throws<InvalidDataException>(() =>
            new BundleResponseValidator().Validate(fixture.Snapshot, response)
        );

        Assert.Equal("Coverage call proof and execution state disagree.", error.Message);
    }

    [Fact]
    public void Individual_call_blocks_are_only_rendered_when_explanations_are_requested()
    {
        var fixture = new ContractFixture();
        var call = new CoverageCallEvidence(
            "document",
            "Shared.cs",
            0x6000001,
            8,
            3,
            false,
            false,
            4,
            false
        );
        var result = new BundleResponseValidator().Validate(
            fixture.Snapshot,
            Response(fixture, call, ExecutionCoverage.Uncovered)
        );
        var renderer = new CompactDiagnosticRenderer(new MockFileSystem());

        var compact = renderer.Render(result);
        var detailed = renderer.Render(result, explainCoverage: true);

        Assert.Equal(
            """
            DP1004 Replace alpha.
            Shared.cs
              1:3

            """.ReplaceLineEndings("\n"),
            compact
        );
        Assert.Equal(
            $"""
            DP1004 Replace alpha.
            Shared.cs
              1:3
                Execution {fixture.Snapshot.Projects[0].Name} net10.0 context=first
                call Shared.cs 0x6000001 IL_0008: block 3 not hit, unproven prefix, completion 4 not hit

            """.ReplaceLineEndings("\n"),
            detailed
        );
    }

    private static BundleResponse Response(
        ContractFixture fixture,
        CoverageCallEvidence call,
        ExecutionCoverage state
    )
    {
        var project = fixture.Snapshot.Projects[0];
        return fixture.Response with
        {
            Contexts =
            [
                new(
                    project.ContextId,
                    true,
                    [
                        fixture.Response.Contexts[0].Findings[0] with
                        {
                            BatchId = null,
                            Coverage =
                            [
                                new(
                                    state,
                                    [],
                                    project.Name,
                                    project.TargetFramework,
                                    project.ContextId
                                )
                                {
                                    Calls = [call],
                                },
                            ],
                        },
                    ]
                ),
                fixture.Response.Contexts[1],
            ],
            Batches = [],
        };
    }
}
