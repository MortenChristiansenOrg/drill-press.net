using System.Text;
using DrillPress.Manifest;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class ContractFixture
{
    public DocumentSnapshot Document { get; } =
        SourceIdentity.Capture(
            new DocumentSnapshot("Shared.cs", "😀alpha\r\nbeta gamma", false)
            {
                DocumentId = "document",
            },
            Encoding.UTF8.GetBytes("😀alpha\r\nbeta gamma"),
            "utf-8",
            false
        );

    public CompilationSnapshot Snapshot { get; }
    public BundleResponse Response { get; }

    public ContractFixture()
    {
        var first = TestSnapshots.CreateProject("Shared.cs", Document.Text) with
        {
            ContextId = "first",
            TargetFramework = "net10.0",
            Documents = [Document],
            ReferencedContextIds = ["second"],
        };
        var second = first with
        {
            ContextId = "second",
            TargetFramework = "net9.0",
            Documents = [Document with { DocumentId = "linked" }],
            ReferencedContextIds = [],
        };
        Snapshot = CompilationSnapshot.Create(first, second) with { RequestId = "request" };
        Response = new BundleResponse(
            1,
            "request",
            [
                new("first", true, [new("DP1004", "Replace alpha.", "document", 2, 5, "fix")]),
                new("second", true, []),
            ],
            [new("fix", [Edit(2, 5, "alpha", "A")], [new("first", true), new("second", true)])]
        );
    }

    public SourceEdit Edit(int start, int length, string original, string replacement) =>
        new(Document.FileIdentity, Document.Fingerprint, start, length, original, replacement);
}
