using System.IO.Abstractions;
using DrillPress.Cli;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class StubSnapshotDirectoryPermissions(IFileSystem fileSystem)
    : SnapshotDirectoryPermissions(fileSystem)
{
    public Action<string> OnRestrict { get; init; } = _ => { };

    internal override void Restrict(string directory) => OnRestrict(directory);
}
