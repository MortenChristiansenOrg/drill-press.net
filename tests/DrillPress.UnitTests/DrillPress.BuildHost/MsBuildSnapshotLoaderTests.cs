using DrillPress.BuildHost;
using Xunit;

namespace DrillPress.UnitTests.BuildHost;

public sealed class MsBuildSnapshotLoaderTests
{
    [Fact]
    public void Public_construction_requires_no_external_dependencies()
    {
        var type = typeof(MsBuildSnapshotLoader);

        var constructors = type.GetConstructors();

        Assert.Equal([0], constructors.Select(constructor => constructor.GetParameters().Length));
    }
}
