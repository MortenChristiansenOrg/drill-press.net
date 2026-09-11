using System.IO.Abstractions.TestingHelpers;
using DrillPress.Benchmarks;
using DrillPress.BundleVerification;
using Xunit;

namespace DrillPress.UnitTests.DrillPress.Benchmarks;

public sealed class ArtifactMeasurementsTests
{
    [Fact]
    public void Counts_and_sorts_nested_publish_files_without_symbols_or_documentation()
    {
        var fileSystem = new MockFileSystem();
        var directory = fileSystem.Path.GetFullPath("publish");
        fileSystem.AddFile(
            fileSystem.Path.Combine(directory, "nested", "dependency.dll"),
            new MockFileData(new byte[] { 4, 5 })
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(directory, "bundle"),
            new MockFileData(new byte[] { 1, 2, 3 })
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(directory, "empty.dll"),
            new MockFileData(Array.Empty<byte>())
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(directory, "bundle.PDB"),
            new MockFileData("debug")
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(directory, "nested", "bundle.dbg"),
            new MockFileData("debug")
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(directory, "bundle.Xml"),
            new MockFileData("docs")
        );
        var measurements = new ArtifactMeasurements(fileSystem);

        var inventory = measurements.Read(BundleMode.Native, directory);

        Assert.Equal(BundleMode.Native, inventory.Mode);
        Assert.Equal(5, inventory.Bytes);
        Assert.Equal(
            [
                new ArtifactFile("bundle", 3),
                new ArtifactFile("empty.dll", 0),
                new ArtifactFile("nested/dependency.dll", 2),
            ],
            inventory.Files
        );
    }

    [Fact]
    public void Reports_an_empty_publish_directory()
    {
        var fileSystem = new MockFileSystem();
        var directory = fileSystem.Path.GetFullPath("empty");
        fileSystem.AddDirectory(directory);
        var measurements = new ArtifactMeasurements(fileSystem);

        var inventory = measurements.Read(BundleMode.Managed, directory);

        Assert.Equal(BundleMode.Managed, inventory.Mode);
        Assert.Equal(0, inventory.Bytes);
        Assert.Empty(inventory.Files);
    }

    [Fact]
    public void Rejects_a_missing_publish_directory()
    {
        var fileSystem = new MockFileSystem();
        var directory = fileSystem.Path.GetFullPath("missing");
        var measurements = new ArtifactMeasurements(fileSystem);

        Assert.Throws<DirectoryNotFoundException>(() =>
            measurements.Read(BundleMode.Native, directory)
        );
    }
}
