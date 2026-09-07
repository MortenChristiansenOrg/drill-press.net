using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class SnapshotWriteFailureFile(MockFileSystem fileSystem, Exception failure) : MockFile(fileSystem)
{
    public override FileSystemStream Open(string path, FileMode mode, FileAccess access, FileShare share) =>
        new SnapshotWriteFailureStream(fileSystem, path, mode, access, share, failure);
}
