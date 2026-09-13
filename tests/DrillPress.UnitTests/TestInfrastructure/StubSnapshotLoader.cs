using System.IO.Abstractions;
using DrillPress.BuildHost;
using DrillPress.Manifest;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class StubSnapshotLoader(IFileSystem fileSystem) : MsBuildSnapshotLoader(fileSystem)
{
    public CompilationSnapshot Snapshot { get; init; } =
        (CompilationSnapshot.Create() with { RequestId = "request" });
    public Exception? Failure { get; init; }
    public SnapshotLoadOptions? Options { get; private set; }
    public string? ProjectPath { get; private set; }

    public override Task<CompilationSnapshot> LoadAsync(
        string projectPath,
        SnapshotLoadOptions options,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProjectPath = projectPath;
        Options = options;
        return Failure is null
            ? Task.FromResult(Snapshot)
            : Task.FromException<CompilationSnapshot>(Failure);
    }
}
