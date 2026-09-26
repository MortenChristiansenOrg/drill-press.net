using DrillPress.Release;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class StubAssemblyVersionReader(ReleasePackageFixture fixture)
    : AssemblyVersionReader
{
    public override string Read(Stream stream) => fixture.AssemblyVersion;
}
