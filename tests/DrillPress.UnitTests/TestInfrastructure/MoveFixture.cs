using System.IO.Abstractions.TestingHelpers;
using System.Text;
using DrillPress.Manifest;

namespace DrillPress.UnitTests.TestInfrastructure;

// An SDK project in an in-memory Orders folder whose glob compiles A.cs and B.cs, with one move batch per file.
internal sealed class MoveFixture
{
    public const string Text = "class A { string Value = string.Empty; }\n";

    public MockFileSystem FileSystem { get; } = new();
    public StubFileIdentityProbe Probe { get; } = new();
    public string Directory { get; }
    public DocumentSnapshot[] Documents { get; }
    public ProjectSnapshot Project { get; }
    public CompilationSnapshot Snapshot { get; }
    public FaultFileReplacer Replacer { get; }
    public FixPlanApplier Applier { get; }

    public MoveFixture()
    {
        Directory = FileSystem.Path.GetFullPath("Orders");
        Documents = new[] { "A.cs", "B.cs" }
            .Select(name =>
            {
                var path = FileSystem.Path.Combine(Directory, name);
                var bytes = Encoding.UTF8.GetBytes(Text);
                FileSystem.AddFile(path, new MockFileData(bytes));
                return SourceIdentity.Capture(
                    new DocumentSnapshot(path, Text, false) { IsDefaultCompileItem = true },
                    bytes,
                    "utf-8",
                    false
                );
            })
            .ToArray();
        FileSystem.AddFile(FileSystem.Path.Combine(Directory, "Orders.csproj"), "<Project />");
        Project = TestSnapshots.CreateProject(Documents[0].Path, Text) with
        {
            ProjectPath = FileSystem.Path.Combine(Directory, "Orders.csproj"),
            TargetFramework = "net10.0",
            Documents = Documents,
            CompileGlob = new(Directory, [FileSystem.Path.Combine(Directory, "bin")]),
        };
        Snapshot = CompilationSnapshot.Create(Project);
        Replacer = new(FileSystem, new SourceFilePolicy(FileSystem, Probe));
        Applier = new(FileSystem, Probe, Replacer);
    }

    public string Path(params string[] segments) =>
        FileSystem.Path.Combine([Directory, .. segments]);

    public SourceMove Move(int document, params string[] destination) =>
        new(Documents[document].FileIdentity, Documents[document].Fingerprint, Path(destination));

    public SourceEdit Edit(int document) =>
        new(
            Documents[document].FileIdentity,
            Documents[document].Fingerprint,
            Text.IndexOf("string.Empty", StringComparison.Ordinal),
            "string.Empty".Length,
            "string.Empty",
            "\"\""
        );

    // One finding per batch, at the file's first character.
    public BundleResponse Response(params FixBatch[] batches) =>
        new(
            BundleResponseProtocol.CurrentVersion,
            Snapshot.RequestId,
            [
                new(
                    Project.ContextId,
                    true,
                    batches
                        .Select(batch => new Finding(
                            "DP1007",
                            "Name the file after its type.",
                            Documents
                                .First(document =>
                                    document.FileIdentity
                                    == (
                                        batch.Moves?.FirstOrDefault()?.FileIdentity
                                        ?? batch.Edits[0].FileIdentity
                                    )
                                )
                                .DocumentId,
                            0,
                            5,
                            batch.Id
                        ))
                        .ToArray()
                ),
            ],
            batches
        );

    public FixBatch Batch(string id, SourceEdit[] edits, params SourceMove[] moves) =>
        new(id, edits, [new(Project.ContextId, true)]) { Moves = moves.Length == 0 ? null : moves };

    public string Json(BundleResponse response) =>
        Encoding.UTF8.GetString(BundleResponseProtocol.Serialize(response));

    public string[] Files() =>
        FileSystem
            .AllFiles.Select(path =>
                FileSystem.Path.GetRelativePath(Directory, path).Replace('\\', '/')
            )
            .Order(StringComparer.Ordinal)
            .ToArray();
}
