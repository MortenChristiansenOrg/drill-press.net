using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using DrillPress.Benchmarks;
using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.DrillPress.Benchmarks;

public sealed class ResultSignaturesTests
{
    [Fact]
    public async Task Comparison_detects_different_context_proofs_hidden_by_identical_aggregation()
    {
        var fileSystem = new MockFileSystem();
        var fixture = new ContractFixture();
        var first = fixture.Response with { Batches = [fixture.Response.Batches[0] with { Validations = [new("first", false), new("second", true)] }] };
        var second = fixture.Response with { Batches = [fixture.Response.Batches[0] with { Validations = [new("first", true), new("second", false)] }] };
        fileSystem.AddFile("snapshot", new(JsonSerializer.Serialize(fixture.Snapshot, CompilationSnapshotJsonContext.Default.CompilationSnapshot)));
        fileSystem.AddFile("first", new(BundleResponseProtocol.Serialize(first)));
        fileSystem.AddFile("second", new(BundleResponseProtocol.Serialize(second)));
        var signatures = new ResultSignatures(fileSystem);
        var expected = await signatures.WriteAsync("snapshot", "first", "out/first", TestContext.Current.CancellationToken);
        var actual = await signatures.WriteAsync("snapshot", "second", "out/second", TestContext.Current.CancellationToken);

        Assert.Throws<InvalidDataException>(() => ResultSignatures.RequireEqual(expected, actual));

        Assert.Equal(expected.PlanHash, actual.PlanHash);
        Assert.Equal(expected.PublicOutputHash, actual.PublicOutputHash);
        Assert.NotEqual(expected.ResponseHash, actual.ResponseHash);
        Assert.Equal(BundleResponseProtocol.Serialize(first), fileSystem.File.ReadAllBytes(expected.ResponsePath));
        Assert.Equal(BundleResponseProtocol.Serialize(second), fileSystem.File.ReadAllBytes(actual.ResponsePath));
    }

    [Fact]
    public async Task Validates_response_association_before_normalizing_run_identity()
    {
        var fileSystem = new MockFileSystem();
        var fixture = new ContractFixture();
        fileSystem.AddFile("snapshot", new(JsonSerializer.Serialize(fixture.Snapshot, CompilationSnapshotJsonContext.Default.CompilationSnapshot)));
        fileSystem.AddFile("response", new(BundleResponseProtocol.Serialize(fixture.Response with { RequestId = "foreign" })));
        var signatures = new ResultSignatures(fileSystem);

        await Assert.ThrowsAsync<InvalidDataException>(() => signatures.WriteAsync("snapshot", "response", "out/result",
            TestContext.Current.CancellationToken, fileSystem.Path.GetFullPath("root")));
    }
}
