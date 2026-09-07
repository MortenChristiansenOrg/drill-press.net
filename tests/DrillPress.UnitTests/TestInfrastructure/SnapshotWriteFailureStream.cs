using System.IO.Abstractions.TestingHelpers;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class SnapshotWriteFailureStream(
    MockFileSystem fileSystem, string path, FileMode mode, FileAccess access, FileShare share, Exception failure)
    : MockFileStream(fileSystem, path, mode, access, share)
{
    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await base.WriteAsync(buffer[..1], cancellationToken);
        throw failure;
    }
}
