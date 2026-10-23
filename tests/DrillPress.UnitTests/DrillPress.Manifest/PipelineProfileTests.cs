using System.Text.Json;
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

        var result = await fixture.Applier.ApplyAsync(
            fixture.Snapshot,
            fixture.Json,
            TestContext.Current.CancellationToken,
            profile
        );

        Assert.Equal(FixApplicationOutcome.Completed, result.Outcome);
        Assert.Equal(fixture.Paths, result.Changed);
        Assert.Equal("profile sink unavailable", profile.Failure);
        Assert.Equal(["😀é\r\nbeta\ngamma\r", "😀é\r\nbeta\ngamma\r"], fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Fact]
    public void Own_measurements_exclude_the_shared_work_they_paid_for()
    {
        using var output = new StringWriter();
        var probe = new ManualProcessProfileProbe();
        var profile = new PipelineProfile(true, output, "rules", probe);

        using (profile.Measure("rule.A", "rule.A.own"))
        {
            probe.Advance(5);
            using (profile.MeasureShared("operations"))
            {
                probe.Advance(20);
                using (profile.MeasureShared("facts"))
                    probe.Advance(7);
            }
            probe.Advance(3);
        }
        using (profile.Measure("rule.B", "rule.B.own"))
            probe.Advance(4);
        profile.WriteShared();

        Assert.Equal(
            [
                ("rule.A", 35.0, 35.0),
                ("rule.A.own", 8.0, 8.0),
                ("rule.B", 4.0, 4.0),
                ("rule.B.own", 4.0, 4.0),
                ("shared.operations", 27.0, 27.0),
            ],
            Events(output.ToString())
        );
    }

    [Fact]
    public async Task Shared_work_on_other_threads_inside_a_shared_scope_is_not_counted_again()
    {
        using var output = new StringWriter();
        var probe = new ManualProcessProfileProbe();
        var profile = new PipelineProfile(true, output, "rules", probe);

        using (profile.MeasureShared("operations"))
        {
            probe.Advance(10);
            await Task.Run(
                () =>
                {
                    using (profile.MeasureShared("facts"))
                        probe.Advance(6);
                },
                TestContext.Current.CancellationToken
            );
        }
        profile.WriteShared();

        Assert.Equal([("shared.operations", 16.0, 16.0)], Events(output.ToString()));
    }

    private static (string, double, double)[] Events(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
                JsonSerializer.Deserialize(
                    line["drillpress-profile ".Length..],
                    CompilationSnapshotJsonContext.Default.ProfileEvent
                )!
            )
            .Select(entry => (entry.Phase, entry.WallMilliseconds, entry.UserCpuMilliseconds))
            .ToArray();
}
