using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.IntegrationTests.Manifest;

public sealed class FixPlanApplierTests
{
    [Fact]
    public async Task Stale_later_file_preserves_all_current_source_bytes()
    {
        using var fixture = new FixApplicationFixture();
        fixture.ChangeSecondFile();

        var result = await fixture.Applier.ApplyAsync(fixture.Snapshot, fixture.Json, TestContext.Current.CancellationToken);

        Assert.Equal(FixApplicationOutcome.PreparationFailed, result.Outcome);
        Assert.Empty(result.Changed);
        Assert.Equal([FixApplicationFixture.Original, "external edit", FixApplicationFixture.Original], fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Fact]
    public async Task Failure_after_writing_second_temporary_file_leaves_every_original_untouched()
    {
        using var fixture = new FixApplicationFixture();
        fixture.Replacer.FailPreparation = true;

        var result = await fixture.Applier.ApplyAsync(fixture.Snapshot, fixture.Json, TestContext.Current.CancellationToken);

        Assert.Equal(FixApplicationOutcome.PreparationFailed, result.Outcome);
        Assert.Empty(result.Changed);
        Assert.Equal([FixApplicationFixture.Original, FixApplicationFixture.Original, FixApplicationFixture.Original], fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Fact]
    public async Task Conflicting_multi_file_batch_is_withheld_and_unrelated_file_is_replaced()
    {
        using var fixture = new FixApplicationFixture();
        var batches = fixture.Response.Batches;
        var response = fixture.Response with { Batches = [batches[0], batches[1] with
        {
            Edits = [.. batches[1].Edits, batches[0].Edits[0] with { Replacement = "null" }],
        }, batches[2]] };

        var result = await fixture.Applier.ApplyAsync(fixture.Snapshot,
            System.Text.Encoding.UTF8.GetString(BundleResponseProtocol.Serialize(response)), TestContext.Current.CancellationToken);

        Assert.Equal(FixApplicationOutcome.Completed, result.Outcome);
        Assert.Equal([fixture.Paths[2]], result.Changed);
        Assert.Equal([FixApplicationFixture.Original, FixApplicationFixture.Original, FixApplicationFixture.Corrected], fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Fact]
    public async Task Non_reporting_target_framework_disagreement_withholds_all_affected_writes()
    {
        using var fixture = new FixApplicationFixture();
        var first = fixture.Snapshot.Projects[0];
        var second = first with { ContextId = "other-framework", TargetFramework = "net9.0", Documents = first.Documents.Select(document =>
            document with { DocumentId = document.DocumentId + "-other" }).ToArray() };
        var snapshot = fixture.Snapshot with { Projects = [first, second] };
        var response = fixture.Response with
        {
            Contexts = [.. fixture.Response.Contexts, new(second.ContextId, true, [])],
            Batches = fixture.Response.Batches.Select(batch => batch with { Validations = [.. batch.Validations, new(second.ContextId, false)] }).ToArray(),
        };

        var result = await fixture.Applier.ApplyAsync(snapshot,
            System.Text.Encoding.UTF8.GetString(BundleResponseProtocol.Serialize(response)), TestContext.Current.CancellationToken);

        Assert.Equal(FixApplicationOutcome.Completed, result.Outcome);
        Assert.Empty(result.Changed);
        Assert.Equal([FixApplicationFixture.Original, FixApplicationFixture.Original, FixApplicationFixture.Original], fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Theory]
    [InlineData(true, false, FixApplicationFixture.Original)]
    [InlineData(false, true, "external edit")]
    public async Task Late_failure_retains_prior_atomic_replacement_and_stops_pending_file(bool failure, bool concurrent, string second)
    {
        using var fixture = new FixApplicationFixture();
        fixture.Replacer.FailReplacement = failure;
        fixture.Replacer.ConcurrentEdit = concurrent;

        var result = await fixture.Applier.ApplyAsync(fixture.Snapshot, fixture.Json, TestContext.Current.CancellationToken);

        Assert.Equal(FixApplicationOutcome.CommitFailed, result.Outcome);
        Assert.Equal([fixture.Paths[0]], result.Changed);
        Assert.Equal(fixture.Paths[1], result.Failed);
        Assert.Equal([fixture.Paths[2]], result.Pending);
        Assert.Equal([FixApplicationFixture.Corrected, second, FixApplicationFixture.Original], fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }
}
