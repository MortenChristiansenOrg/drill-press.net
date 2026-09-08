using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Manifest;

public sealed class PipelineProfileTests
{
    [Fact]
    public async Task Profile_output_failure_cannot_interrupt_preparation_commit_or_cleanup()
    {
        var fixture = new FixFixture();
        using var output = new ProfileFailureWriter();
        var profile = new PipelineProfile(true, output, "fix", new StubProcessProfileProbe());

        var result = await fixture.Applier.ApplyAsync(fixture.Snapshot, fixture.Json, TestContext.Current.CancellationToken, profile);

        Assert.Equal(FixApplicationOutcome.Completed, result.Outcome);
        Assert.Equal(fixture.Paths, result.Changed);
        Assert.Equal("profile sink unavailable", profile.Failure);
        Assert.Equal(["😀é\r\nbeta\ngamma\r", "😀é\r\nbeta\ngamma\r"], fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }
}
