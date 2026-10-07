using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using DrillPress.Engine;
using DrillPress.Manifest;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class CoverageFixture
{
    public const string Source = """
        namespace System { public class Object {} public struct Void {} public struct Boolean {} }
        class C
        {
            static void Hit() { }
            void Run(bool condition)
            {
                Hit();
                condition.ToString();
                var result = condition ? HitValue() : HitValue();
            }
            static C HitValue() => null;
        }
        """;

    public MockFileSystem FileSystem { get; } = new();
    public FakeCoverageProcess Process { get; }
    public CompilationSnapshot Snapshot { get; }

    public CoverageFixture(
        string source = Source,
        params (string Path, string Text)[] additionalSources
    )
    {
        var path = FileSystem.Path.GetFullPath("/coverage/Target.cs");
        var project = TestSnapshots.CreateProject(path, source) with
        {
            TargetFramework = "net10.0",
        };
        project = project with
        {
            Documents =
            [
                .. project.Documents,
                .. additionalSources.Select(extra =>
                    SourceIdentity.Capture(
                        new DocumentSnapshot(
                            FileSystem.Path.GetFullPath(
                                extra.Path,
                                FileSystem.Path.GetDirectoryName(path)!
                            ),
                            extra.Text,
                            false
                        ),
                        Encoding.UTF8.GetBytes(extra.Text),
                        "utf-8",
                        false
                    )
                ),
            ],
        };
        Snapshot = CompilationSnapshot.Create(project);
        FileSystem.AddFile(path, new MockFileData(source));
        foreach (var document in project.Documents.Skip(1))
            FileSystem.AddFile(document.Path, new MockFileData(document.Text));
        FileSystem.AddFile(project.ProjectPath, new MockFileData("<Project />"));
        FileSystem.AddFile(
            FileSystem.Path.GetFullPath("/coverage/Tests.csproj"),
            new MockFileData("<Project />")
        );
        FileSystem.AddFile(
            FileSystem.Path.GetFullPath("/coverage/Workspace.slnx"),
            new MockFileData("<Solution />")
        );
        var metadata = new MetadataBuilder();
        foreach (var document in project.Documents)
            metadata.AddDocument(
                metadata.GetOrAddDocumentName(document.Path),
                metadata.GetOrAddGuid(new Guid("8829d00f-11b8-4213-878b-770e8597ac16")),
                metadata.GetOrAddBlob(SHA256.HashData(SourceIdentity.Encode(document))),
                default
            );
        var compilerOptions = Encoding.UTF8.GetBytes(
            "language\0C#\0output-kind\0DynamicallyLinkedLibrary\0platform\0AnyCpu\0nullable\0Enable\0language-version\014.0\0"
        );
        metadata.AddCustomDebugInformation(
            EntityHandle.ModuleDefinition,
            metadata.GetOrAddGuid(new Guid("b5feec05-8cd0-4a83-96da-466284bb4bd8")),
            metadata.GetOrAddBlob(compilerOptions)
        );
        var rows = new int[64];
        rows[0] = 1;
        var builder = new PortablePdbBuilder(metadata, ImmutableArray.Create(rows), default);
        var blob = new BlobBuilder();
        var pdbId = builder.Serialize(blob);
        var symbolBytes = blob.ToArray();
        FileSystem.AddFile(
            FileSystem.Path.ChangeExtension(project.ProjectPath, ".pdb"),
            new MockFileData(symbolBytes)
        );
        using var stream = new MemoryStream(symbolBytes);
        using var reader = MetadataReaderProvider.FromPortablePdbStream(stream);
        var identity = Convert.ToHexString(
            reader.GetMetadataReader().DebugMetadataHeader!.Id.AsSpan()
        );
        Process = new(FileSystem, project, identity);
        var module = new MetadataBuilder();
        module.AddModule(
            0,
            module.GetOrAddString(project.AssemblyName + ".dll"),
            module.GetOrAddGuid(Guid.NewGuid()),
            default,
            default
        );
        var debug = new DebugDirectoryBuilder();
        debug.AddCodeViewEntry(
            FileSystem.Path.ChangeExtension(project.ProjectPath, ".pdb"),
            pdbId,
            0x0100
        );
        var pe = new ManagedPEBuilder(
            new PEHeaderBuilder(),
            new MetadataRootBuilder(module),
            new BlobBuilder(),
            debugDirectoryBuilder: debug
        );
        var assembly = new BlobBuilder();
        pe.Serialize(assembly);
        FileSystem.AddFile(
            FileSystem.Path.ChangeExtension(project.ProjectPath, ".dll"),
            new MockFileData(assembly.ToArray())
        );
    }

    public AnalysisEngine Engine() => new(FileSystem, Process);

    public static RuleSet ExecutionRules()
    {
        var rules = new RuleSet();
        rules
            .Rule("COV001", "Exercise call.")
            .For(Code.Calls.Where(call => call.Target.Name is "Hit" or "HitValue"))
            .Require(Coverage.Executed);
        return rules;
    }
}
