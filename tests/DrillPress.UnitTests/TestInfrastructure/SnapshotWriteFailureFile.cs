using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class SnapshotWriteFailureFile(MockFileSystem fileSystem, Exception failure) : MockFile(fileSystem)
{
    public override FileSystemStream Open(string path, FileStreamOptions options) =>
        new SnapshotWriteFailureStream(fileSystem, path, options.Mode, options.Access, options.Share, failure);
}
