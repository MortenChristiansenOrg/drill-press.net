using DrillPress.BuildHost;
using DrillPress.Manifest;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class StubSnapshotLoader : ICompilationSnapshotLoader
{
    public CompilationSnapshot Snapshot { get; init; } = CompilationSnapshot.Create();
    public Exception? Failure { get; init; }
    public string? ProjectPath { get; private set; }

    public Task<CompilationSnapshot> LoadAsync(string projectPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProjectPath = projectPath;
        return Failure is null
            ? Task.FromResult(Snapshot)
            : Task.FromException<CompilationSnapshot>(Failure);
    }
}
