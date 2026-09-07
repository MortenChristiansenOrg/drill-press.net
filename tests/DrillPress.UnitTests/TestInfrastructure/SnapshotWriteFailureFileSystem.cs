using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class SnapshotWriteFailureFileSystem : MockFileSystem
{
    public SnapshotWriteFailureFileSystem(Exception failure)
    {
        File = new SnapshotWriteFailureFile(this, failure);
    }

    public override IFile File { get; }
}
