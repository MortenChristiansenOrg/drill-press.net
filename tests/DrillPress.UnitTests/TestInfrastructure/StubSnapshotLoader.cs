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
    public IReadOnlyList<string>? Targets { get; private set; }

    internal override Task<CompilationSnapshot> LoadAsync(
        IReadOnlyList<string> targets,
        SnapshotLoadOptions options,
        PipelineProfile profile,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Targets = targets;
        Options = options;
        return Failure is null
            ? Task.FromResult(Snapshot)
            : Task.FromException<CompilationSnapshot>(Failure);
    }
}
