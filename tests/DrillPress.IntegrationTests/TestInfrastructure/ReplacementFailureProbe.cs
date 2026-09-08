using System.IO.Abstractions;
using DrillPress.Manifest;

namespace DrillPress.IntegrationTests.TestInfrastructure;

internal sealed class ReplacementFailureProbe(IFileSystem fileSystem, SourceFilePolicy policy, string failurePath) : AtomicFileReplacer(fileSystem, policy)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    public bool FailPreparation { get; set; }
    public bool FailReplacement { get; set; }
    public bool ConcurrentEdit { get; set; }

    internal override async Task PrepareAsync(PreparedSourceFile file, CancellationToken cancellationToken)
    {
        await base.PrepareAsync(file, cancellationToken);
        if (FailPreparation && file.Path == failurePath) throw new IOException("injected preparation failure");
    }

    internal override Task ReplaceAsync(CompilationSnapshot snapshot, PreparedSourceFile file, CancellationToken cancellationToken)
    {
        if (file.Path == failurePath)
        {
            if (FailReplacement) throw new IOException("injected replacement failure");
            if (ConcurrentEdit) _fileSystem.File.WriteAllText(file.Path, "external edit");
        }

        return base.ReplaceAsync(snapshot, file, cancellationToken);
    }
}
