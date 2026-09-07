using DrillPress.Manifest;

namespace DrillPress.BuildHost;

/// <summary>Separates external project evaluation from snapshot file handling.</summary>
public interface ICompilationSnapshotLoader
{
    /// <summary>Exports effective compiler inputs from an absolute C# project path.</summary>
    Task<CompilationSnapshot> LoadAsync(string projectPath, CancellationToken cancellationToken);
}
