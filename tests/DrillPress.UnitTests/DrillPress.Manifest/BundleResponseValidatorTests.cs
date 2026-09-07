using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Manifest;

public sealed class BundleResponseValidatorTests
{
    private readonly ContractFixture _fixture = new();

    [Fact]
    public void Non_reporting_linked_context_participates_in_fix_validation()
    {
        var batch = _fixture.Response.Batches[0] with { Validations = [new("first", true), new("second", false)] };
        var response = _fixture.Response with { Batches = [batch] };

        var result = new BundleResponseValidator().Validate(_fixture.Snapshot, response);

        Assert.Equal([new AggregatedFinding("DP1004", "Replace alpha.", "Shared.cs", "Shared.cs", 2, 5, 1, 3, null)], result.Findings);
        Assert.Empty(result.Edits);
        Assert.Empty(result.Batches);
    }

    [Fact]
    public void Missing_affected_context_validation_withholds_the_batch()
    {
        var response = _fixture.Response with { Batches = [_fixture.Response.Batches[0] with { Validations = [new("first", true)] }] };

        var result = new BundleResponseValidator().Validate(_fixture.Snapshot, response);

        Assert.Null(Assert.Single(result.Findings).BatchId);
        Assert.Empty(result.Edits);
    }

    [Fact]
    public void Linked_findings_and_identical_edits_are_deduplicated()
    {
        var response = _fixture.Response with
        {
            Contexts = [_fixture.Response.Contexts[0], new("second", true, [new("DP1004", "Replace alpha.", "linked", 2, 5, "copy")])],
            Batches = [_fixture.Response.Batches[0], _fixture.Response.Batches[0] with { Id = "copy", Edits = [_fixture.Response.Batches[0].Edits[0], _fixture.Response.Batches[0].Edits[0]] }],
        };

        var result = new BundleResponseValidator().Validate(_fixture.Snapshot, response);

        Assert.Equal([new AggregatedFinding("DP1004", "Replace alpha.", "Shared.cs", "Shared.cs", 2, 5, 1, 3, "fix")], result.Findings);
        Assert.Equal([_fixture.Edit(2, 5, "alpha", "A")], result.Edits);
    }

    [Theory]
    [InlineData(2, 5, "alpha", "other")]
    [InlineData(3, 1, "l", "other")]
    [InlineData(2, 0, "", "insert")]
    [InlineData(7, 0, "", "insert")]
    public void Conflicts_withhold_entire_batches_and_keep_independent_fixes(int start, int length, string original, string replacement)
    {
        var validations = _fixture.Response.Batches[0].Validations;
        var response = _fixture.Response with
        {
            Contexts = [new("first", true,
                [new("DP1004", "Replace alpha.", "document", 2, 5, "fix"),
                 new("DP1004", "Replace alpha.", "document", 9, 4, "conflict"),
                 new("DP1004", "Replace alpha.", "document", 14, 5, "independent")]), _fixture.Response.Contexts[1]],
            Batches = [_fixture.Response.Batches[0],
                new("conflict", [_fixture.Edit(start, length, original, replacement), _fixture.Edit(9, 4, "beta", "B")], validations),
                new("independent", [_fixture.Edit(14, 5, "gamma", "G")], validations)],
        };

        var result = new BundleResponseValidator().Validate(_fixture.Snapshot, response);

        Assert.Equal([null, null, "independent"], result.Findings.Select(finding => finding.BatchId));
        Assert.Equal([_fixture.Edit(14, 5, "gamma", "G")], result.Edits);
        Assert.Equal(["independent"], result.Batches.Select(batch => batch.Id));
    }

    [Fact]
    public void Reporting_context_disagreement_withholds_an_otherwise_validated_fix()
    {
        var response = _fixture.Response with
        {
            Contexts = [_fixture.Response.Contexts[0], new("second", true, [new("DP1004", "Replace alpha.", "linked", 2, 5, null)])],
        };

        var result = new BundleResponseValidator().Validate(_fixture.Snapshot, response);

        Assert.Null(Assert.Single(result.Findings).BatchId);
        Assert.Empty(result.Edits);
    }

    [Theory]
    [InlineData("{\"protocolVersion\":1}")]
    [InlineData("null")]
    [InlineData("garbage")]
    [InlineData("{\"protocolVersion\":1,\"requestId\":\"other\",\"contexts\":[],\"batches\":[]}")]
    public void Malformed_and_foreign_responses_are_rejected(string response)
    {
        Assert.ThrowsAny<Exception>(() => BundleResponseProtocol.Read(response, _fixture.Snapshot));
    }

    public static TheoryData<Func<BundleResponse, BundleResponse>> InvalidResponses => new()
    {
        response => response with { ProtocolVersion = 99 },
        response => response with { RequestId = "other" },
        response => response with { Contexts = [response.Contexts[0]] },
        response => response with { Contexts = [response.Contexts[0], response.Contexts[1] with { IsComplete = false }] },
        response => response with { Contexts = [response.Contexts[0] with { Findings = [response.Contexts[0].Findings[0] with { DocumentId = "linked" }] }, response.Contexts[1]] },
        response => response with { Contexts = [response.Contexts[0] with { Findings = [response.Contexts[0].Findings[0] with { Start = int.MaxValue }] }, response.Contexts[1]] },
        response => response with { Batches = [response.Batches[0] with { Edits = [response.Batches[0].Edits[0] with { Fingerprint = "stale" }] }] },
        response => response with { Batches = [response.Batches[0] with { Edits = [response.Batches[0].Edits[0] with { FileIdentity = "foreign" }] }] },
    };

    [Theory]
    [MemberData(nameof(InvalidResponses))]
    public void Invalid_source_association_or_evaluation_is_rejected(Func<BundleResponse, BundleResponse> change)
    {
        var response = change(_fixture.Response);

        Assert.Throws<InvalidDataException>(() => new BundleResponseValidator().Validate(_fixture.Snapshot, response));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_edit_in_a_different_noneditable_document_is_rejected(bool isGenerated)
    {
        var generated = new DocumentSnapshot("Generated.cs", "alpha", isGenerated) { DocumentId = "generated" };
        var snapshot = _fixture.Snapshot with { Projects = [_fixture.Snapshot.Projects[0] with { Documents = [_fixture.Document, generated] }, _fixture.Snapshot.Projects[1]] };
        var response = _fixture.Response with { Batches = [_fixture.Response.Batches[0] with { Edits = [new("Generated.cs", "", 0, 5, "alpha", "A")] }] };

        Assert.Throws<InvalidDataException>(() => new BundleResponseValidator().Validate(snapshot, response));
    }
}
