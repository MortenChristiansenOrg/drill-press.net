using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class FileLengthFailureFileSystem : MockFileSystem
{
    public FileLengthFailureFileSystem() =>
        FileInfo = new MockFileInfoFactory(new MockFileSystem());

    public override IFileInfoFactory FileInfo { get; }
}
