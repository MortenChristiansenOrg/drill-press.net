using System.IO.Abstractions;
using DrillPress.Manifest;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class FaultFileReplacer(IFileSystem fileSystem, SourceFilePolicy policy)
    : AtomicFileReplacer(fileSystem, policy)
{
    public string? PreparationFailure { get; set; }
    public Dictionary<string, Action> OnReplacement { get; } = [];
    public List<string> Replacements { get; } = [];

    internal override Task PrepareAsync(
        PreparedSourceFile file,
        CancellationToken cancellationToken
    )
    {
        if (file.Path == PreparationFailure)
            throw new IOException("disk full");
        return base.PrepareAsync(file, cancellationToken);
    }

    internal override async Task ReplaceAsync(
        CompilationSnapshot snapshot,
        PreparedSourceFile file,
        CancellationToken cancellationToken
    )
    {
        if (OnReplacement.TryGetValue(file.Path, out var action))
            action();
        await base.ReplaceAsync(snapshot, file, cancellationToken);
        Replacements.Add(file.Path);
    }
}
