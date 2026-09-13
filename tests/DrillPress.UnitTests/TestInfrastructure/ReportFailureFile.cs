using System.IO.Abstractions.TestingHelpers;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class ReportFailureFile(MockFileSystem fileSystem, IOException failure)
    : MockFile(fileSystem)
{
    private readonly MockFileSystem _fileSystem = fileSystem;
    private readonly IOException _failure = failure;

    public override Task WriteAllTextAsync(
        string path,
        string? contents,
        CancellationToken cancellationToken = default
    ) =>
        _fileSystem.Path.GetFileName(path) == "report.json"
            ? Task.FromException(_failure)
            : base.WriteAllTextAsync(path, contents, cancellationToken);
}
