using System.Text;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.UnitTests.Manifest;

public sealed class BundleResponseProtocolTests
{
    [Fact]
    public void Clean_internal_golden_is_exact_UTF8_JSON()
    {
        var response = new BundleResponse(1, "request", [new("context", true, [])], []);

        var output = BundleResponseProtocol.Serialize(response);

        Assert.Equal(
            Encoding.UTF8.GetBytes(
                """{"protocolVersion":1,"requestId":"request","contexts":[{"contextId":"context","isComplete":true,"findings":[]}],"batches":[]}"""
            ),
            output
        );
    }

    [Fact]
    public void Fixable_linked_internal_golden_locks_complete_batches_and_nonreporting_validation()
    {
        var response = new BundleResponse(
            1,
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
                """{"protocolVersion":1,"requestId":"request","contexts":[{"contextId":"first","isComplete":true,"findings":[{"ruleId":"R","message":"Replace.","documentId":"doc","start":2,"length":5,"batchId":"fix"}]},{"contextId":"second","isComplete":true,"findings":[]}],"batches":[{"id":"fix","edits":[{"fileIdentity":"file","fingerprint":"hash","start":2,"length":5,"originalText":"alpha","replacement":"A"}],"validations":[{"contextId":"first","isSafe":true},{"contextId":"second","isSafe":false}]}]}"""
            ),
            output
        );
    }

    [Fact]
    public void Nonfixable_single_context_internal_golden_is_exact()
    {
        var response = new BundleResponse(
            1,
            "request",
            [new("context", true, [new("R", "Replace.", "doc", 2, 5, null)])],
            []
        );

        var output = BundleResponseProtocol.Serialize(response);

        Assert.Equal(
            Encoding.UTF8.GetBytes(
                """{"protocolVersion":1,"requestId":"request","contexts":[{"contextId":"context","isComplete":true,"findings":[{"ruleId":"R","message":"Replace.","documentId":"doc","start":2,"length":5,"batchId":null}]}],"batches":[]}"""
            ),
            output
        );
    }
}
