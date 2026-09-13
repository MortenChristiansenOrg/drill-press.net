using System.IO.Abstractions.TestingHelpers;
using System.Text;
using DrillPress.Manifest;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class FixFixture
{
    public MockFileSystem FileSystem { get; } = new();
    public StubFileIdentityProbe Probe { get; } = new();
    public CompilationSnapshot Snapshot { get; }
    public BundleResponse Response { get; }
    public DocumentSnapshot[] Documents { get; }
    public FaultFileReplacer Replacer { get; }
    public FixPlanApplier Applier { get; }
    public string Json => Encoding.UTF8.GetString(BundleResponseProtocol.Serialize(Response));
    public string[] Paths => Documents.Select(document => document.Path).ToArray();

    public FixFixture(
        string encodingName = "utf-8",
        bool bom = false,
        string replacement = "é",
        string text = "😀alpha\r\nbeta\ngamma\r",
        int fileCount = 2
    )
    {
        var encoding = Encoding.GetEncoding(encodingName);
        Documents = Enumerable
            .Range(0, fileCount)
            .Select(index => $"{(char)('A' + index)}.cs")
            .Select(name =>
            {
                var path = FileSystem.Path.GetFullPath(name);
                var content = encoding.GetBytes(text);
                byte[] bytes = bom ? [.. encoding.GetPreamble(), .. content] : content;
                FileSystem.AddFile(path, new MockFileData(bytes));
                return SourceIdentity.Capture(
                    new DocumentSnapshot(path, text, false),
                    bytes,
                    encodingName,
                    bom
                );
            })
            .ToArray();
        var project = TestSnapshots.CreateProject(Paths[0], text) with { Documents = Documents };
        Snapshot = CompilationSnapshot.Create(project);
        var batches = Documents
            .Select(
                (document, index) =>
                    new FixBatch(
                        $"fix{index}",
                        [
                            new(
                                document.FileIdentity,
                                document.Fingerprint,
                                text.IndexOf("alpha", StringComparison.Ordinal),
                                5,
                                "alpha",
                                replacement
                            ),
                        ],
                        [new(project.ContextId, true)]
                    )
            )
            .ToArray();
        Response = new(
            1,
            Snapshot.RequestId,
            [
                new(
                    project.ContextId,
                    true,
                    Documents
                        .Select(
                            (document, index) =>
                                new Finding(
                                    "DP1004",
                                    "Replace alpha.",
                                    document.DocumentId,
                                    text.IndexOf("alpha", StringComparison.Ordinal),
                                    5,
                                    $"fix{index}"
                                )
                        )
                        .ToArray()
                ),
            ],
            batches
        );
        Replacer = new(FileSystem, new SourceFilePolicy(FileSystem, Probe));
        Applier = new(FileSystem, Probe, Replacer);
    }

    public string[] Texts() => Paths.Select(FileSystem.File.ReadAllText).ToArray();

    public string[] TemporaryFiles() => FileSystem.AllFiles.Except(Paths).ToArray();
}
