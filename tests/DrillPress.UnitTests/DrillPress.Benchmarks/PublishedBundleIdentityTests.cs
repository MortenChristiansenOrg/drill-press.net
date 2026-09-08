using System.IO.Abstractions.TestingHelpers;
using DrillPress.Benchmarks;
using Xunit;

namespace DrillPress.UnitTests.DrillPress.Benchmarks;

public sealed class PublishedBundleIdentityTests
{
    [Fact]
    public void Fingerprints_dependencies_as_well_as_the_entry_assembly()
    {
        var fileSystem = new MockFileSystem();
        var entry = fileSystem.Path.GetFullPath("bundle/entry.dll");
        fileSystem.AddFile(entry, new("abc"));
        fileSystem.AddFile(fileSystem.Path.GetFullPath("bundle/nested/dependency.dll"), new(""));
        var identity = new PublishedBundleIdentity(fileSystem);

        var result = identity.Read(entry);

        Assert.Equal(entry, result.EntryPoint);
        Assert.Equal([
            new ArtifactFingerprint("entry.dll", 3, "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"),
            new ArtifactFingerprint("nested/dependency.dll", 0, "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855"),
        ], result.Files);
    }
}
