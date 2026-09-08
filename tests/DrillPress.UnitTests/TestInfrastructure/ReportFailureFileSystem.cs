using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class ReportFailureFileSystem : MockFileSystem
{
    public IOException ReportFailure { get; } = new("Report storage failed.");

    public override IFile File => new ReportFailureFile(this, ReportFailure);
}
