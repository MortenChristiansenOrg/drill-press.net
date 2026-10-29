using System.IO.Abstractions;
using DrillPress.BuildHost;
using DrillPress.Engine;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class FakeCaptureSnapshotLoader(IFileSystem fileSystem)
    : MsBuildSnapshotLoader(fileSystem, new StubFileIdentityProbe())
{
    private readonly IFileSystem _fileSystem = fileSystem;

    internal int Captures { get; private set; }

    internal Action? DuringCapture { get; set; }

    internal override Task<SnapshotExport> CaptureAsync(
        string target,
        SnapshotLoadOptions options,
        PipelineProfile profile,
        CancellationToken cancellationToken
    )
    {
        Captures++;
        var source = TestSnapshots.CreateProject(
            "/repo/Source.cs",
            _fileSystem.File.ReadAllText("/repo/Source.cs")
        ) with
        {
            ProjectPath = target,
        };
        DuringCapture?.Invoke();
        var compilation = CSharpCompilation.Create(
            "Target",
            [CSharpSyntaxTree.ParseText(source.Documents[0].Text, path: source.Documents[0].Path)]
        );
        return Task.FromResult(
            new SnapshotExport(
                CompilationSnapshot.Create(source),
                [new CompilationContext(source, compilation)]
            )
            {
                InputPaths = [target, "/shared/input.txt"],
                InputDirectories = ["/resources"],
            }
        );
    }
}
