using System.Text;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress.IntegrationTests.TestInfrastructure;

internal sealed class FixApplicationFixture : IntegrationTest
{
    public string DirectoryPath { get; }
    public string[] Paths { get; }
    public CompilationSnapshot Snapshot { get; }
    public BundleResponse Response { get; }
    public string Json => Encoding.UTF8.GetString(BundleResponseProtocol.Serialize(Response));
    public FixPlanApplier Applier { get; }
    public ReplacementFailureProbe Replacer { get; }
    public const string Original = "class A { string Value = string.Empty; }\r\n";
    public const string Corrected = "class A { string Value = \"\"; }\r\n";

    public FixApplicationFixture()
    {
        DirectoryPath = CreateTemporaryDirectory("drillpress-write-failure-").FullName;
        Paths = new[] { "A.cs", "B.cs", "C.cs" }
            .Select(name => FileSystem.Path.Combine(DirectoryPath, name))
            .ToArray();
        foreach (var path in Paths)
            FileSystem.File.WriteAllBytes(path, Encoding.UTF8.GetBytes(Original));
        var documents = Paths
            .Select(path =>
                SourceIdentity.Capture(
                    new DocumentSnapshot(path, Original, false),
                    FileSystem.File.ReadAllBytes(path),
                    "utf-8",
                    false
                )
            )
            .ToArray();
        var project = new ProjectSnapshot(
            "Fixture",
            "Fixture",
            FileSystem.Path.Combine(DirectoryPath, "Fixture.csproj"),
            (int)LanguageVersion.CSharp14,
            (int)OutputKind.DynamicallyLinkedLibrary,
            (int)NullableContextOptions.Enable,
            [],
            documents,
            []
        );
        Snapshot = CompilationSnapshot.Create(project);
        var batches = documents
            .Select(
                (document, index) =>
                    new FixBatch(
                        $"fix{index}",
                        [
                            new(
                                document.FileIdentity,
                                document.Fingerprint,
                                25,
                                12,
                                "string.Empty",
                                "\"\""
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
                    documents
                        .Select(
                            (document, index) =>
                                new Finding(
                                    "DP1004",
                                    "Use literal.",
                                    document.DocumentId,
                                    25,
                                    12,
                                    $"fix{index}"
                                )
                        )
                        .ToArray()
                ),
            ],
            batches
        );
        var probe = new FileIdentityProbe();
        Replacer = new(FileSystem, new SourceFilePolicy(FileSystem, probe), Paths[1]);
        Applier = new(FileSystem, probe, Replacer);
    }

    public string[] Texts() => Paths.Select(FileSystem.File.ReadAllText).ToArray();

    public string[] TemporaryFiles() => FileSystem.Directory.GetFiles(DirectoryPath, ".dp-*.tmp");

    public void ChangeSecondFile() => FileSystem.File.WriteAllText(Paths[1], "external edit");
}
