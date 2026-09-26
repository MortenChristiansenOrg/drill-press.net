using System.Reflection;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using DrillPress.Release;
using Xunit;

namespace DrillPress.IntegrationTests.Release;

public sealed class AssemblyVersionReaderTests : IntegrationTest
{
    [Fact]
    public void Reads_full_version_from_real_assembly_metadata()
    {
        var assembly = typeof(CompilationSnapshot).Assembly;
        using var stream = FileSystem.File.OpenRead(assembly.Location);
        var reader = new AssemblyVersionReader();
        var expected = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        var version = reader.Read(stream);

        Assert.Equal(expected, version);
        Assert.Equal(expected, CompilationSnapshot.Create().ProductVersion);
    }
}
