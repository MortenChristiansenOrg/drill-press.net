using DrillPress.Benchmarks;
using DrillPress.BundleVerification;
using Xunit;

namespace DrillPress.IntegrationTests;

public sealed class ArtifactMeasurementsTests : IntegrationTest
{
    [Fact]
    public async Task Counts_complete_publish_inventory_without_debug_or_documentation_files()
    {
        var directory = CreateTemporaryDirectory("drillpress-artifact-size-");
        var cancellationToken = TestContext.Current.CancellationToken;
        var nested = Directory.CreateDirectory(Path.Combine(directory.FullName, "nested"));
        await File.WriteAllBytesAsync(Path.Combine(directory.FullName, "bundle"), [1, 2, 3], cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(nested.FullName, "dependency.dll"), [4, 5], cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory.FullName, "bundle.PDB"), "debug", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory.FullName, "bundle.dbg"), "debug", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory.FullName, "bundle.xml"), "docs", cancellationToken);

        var inventory = ArtifactMeasurements.Read(BundleMode.Native, directory.FullName);

        Assert.Equal(BundleMode.Native, inventory.Mode);
        Assert.Equal(5, inventory.Bytes);
        Assert.Equal([new ArtifactFile("bundle", 3), new ArtifactFile("nested/dependency.dll", 2)], inventory.Files);
    }
}
