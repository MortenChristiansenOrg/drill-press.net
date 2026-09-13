using System.Text;
using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Manifest;

public sealed class FixPlanApplierTests
{
    [Theory]
    [InlineData("utf-8", false)]
    [InlineData("utf-8", true)]
    [InlineData("utf-16", true)]
    [InlineData("utf-16BE", true)]
    [InlineData("utf-32", false)]
    public async Task Replaces_all_files_preserving_encoding_bom_and_mixed_line_endings(
        string encoding,
        bool bom
    )
    {
        var fixture = new FixFixture(encoding, bom);
        var expected = fixture
            .Documents.Select(document =>
                SourceIdentity.Encode(document with { Text = "😀é\r\nbeta\ngamma\r" })
            )
            .ToArray();

        var result = await fixture.Applier.ApplyAsync(
            fixture.Snapshot,
            fixture.Json,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(FixApplicationOutcome.Completed, result.Outcome);
        Assert.Equal(fixture.Paths, result.Changed);
        Assert.Null(result.Failed);
        Assert.Empty(result.Pending);
        Assert.Null(result.Error);
        Assert.Equal(expected, fixture.Paths.Select(fixture.FileSystem.File.ReadAllBytes));
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Fact]
    public async Task Duplicate_edits_are_applied_once()
    {
        var fixture = new FixFixture();
        var response = fixture.Response with
        {
            Batches = fixture
                .Response.Batches.Select(batch =>
                    batch with
                    {
                        Edits = [.. batch.Edits, .. batch.Edits],
                    }
                )
                .ToArray(),
        };

        var result = await fixture.Applier.ApplyAsync(
            fixture.Snapshot,
            Encoding.UTF8.GetString(BundleResponseProtocol.Serialize(response)),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(FixApplicationOutcome.Completed, result.Outcome);
        Assert.Equal(["😀é\r\nbeta\ngamma\r", "😀é\r\nbeta\ngamma\r"], fixture.Texts());
        Assert.Equal(fixture.Paths, fixture.Replacer.Replacements);
    }

    [Fact]
    public async Task Splitting_a_surrogate_pair_leaves_all_originals_untouched()
    {
        var fixture = new FixFixture();
        var response = fixture.Response with
        {
            Batches = fixture
                .Response.Batches.Select(batch =>
                    batch with
                    {
                        Edits =
                        [
                            batch.Edits[0] with
                            {
                                Start = 1,
                                Length = 0,
                                OriginalText = "",
                                Replacement = "X",
                            },
                        ],
                    }
                )
                .ToArray(),
        };

        var result = await fixture.Applier.ApplyAsync(
            fixture.Snapshot,
            Encoding.UTF8.GetString(BundleResponseProtocol.Serialize(response)),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(FixApplicationOutcome.PreparationFailed, result.Outcome);
        Assert.Empty(result.Changed);
        Assert.Equal(fixture.Documents.Select(document => document.Text), fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Fact]
    public async Task Unrepresentable_replacement_aborts_before_any_source_write()
    {
        var fixture = new FixFixture("us-ascii", replacement: "é", text: "alpha\r\nbeta\n");

        var result = await fixture.Applier.ApplyAsync(
            fixture.Snapshot,
            fixture.Json,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(FixApplicationOutcome.PreparationFailed, result.Outcome);
        Assert.Empty(result.Changed);
        Assert.Equal(fixture.Documents.Select(document => document.Text), fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Fact]
    public async Task Stale_later_source_aborts_before_any_preparation_or_replacement()
    {
        var fixture = new FixFixture();
        fixture.FileSystem.File.WriteAllText(fixture.Paths[1], "external edit");

        var result = await fixture.Applier.ApplyAsync(
            fixture.Snapshot,
            fixture.Json,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(FixApplicationOutcome.PreparationFailed, result.Outcome);
        Assert.Empty(result.Changed);
        Assert.Equal(fixture.Paths[1], result.Failed);
        Assert.Equal([fixture.Paths[0]], result.Pending);
        Assert.Equal([fixture.Documents[0].Text, "external edit"], fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Fact]
    public async Task Later_preparation_failure_cleans_staged_files_without_replacing_originals()
    {
        var fixture = new FixFixture();
        fixture.Replacer.PreparationFailure = fixture.Paths[1];

        var result = await fixture.Applier.ApplyAsync(
            fixture.Snapshot,
            fixture.Json,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(FixApplicationOutcome.PreparationFailed, result.Outcome);
        Assert.Empty(result.Changed);
        Assert.Equal("disk full", result.Error);
        Assert.Equal(fixture.Documents.Select(document => document.Text), fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Fact]
    public async Task Late_external_edit_is_retained_and_earlier_replacement_reported()
    {
        var fixture = new FixFixture();
        fixture.Replacer.OnReplacement[fixture.Paths[1]] = () =>
            fixture.FileSystem.File.WriteAllText(fixture.Paths[1], "external edit");

        var result = await fixture.Applier.ApplyAsync(
            fixture.Snapshot,
            fixture.Json,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(FixApplicationOutcome.CommitFailed, result.Outcome);
        Assert.Equal([fixture.Paths[0]], result.Changed);
        Assert.Equal(fixture.Paths[1], result.Failed);
        Assert.Empty(result.Pending);
        Assert.Equal("Source identity or bytes changed before replacement.", result.Error);
        Assert.Equal(["😀é\r\nbeta\ngamma\r", "external edit"], fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Fact]
    public async Task Conflicting_multi_file_batch_is_wholly_withheld_while_unrelated_edit_is_applied()
    {
        var fixture = new FixFixture();
        var first = fixture.Response.Batches[0];
        var second = fixture.Response.Batches[1];
        var independent = second with
        {
            Id = "independent",
            Edits =
            [
                second.Edits[0] with
                {
                    Start = 9,
                    Length = 4,
                    OriginalText = "beta",
                    Replacement = "B",
                },
            ],
        };
        var context = fixture.Response.Contexts[0];
        var response = fixture.Response with
        {
            Batches =
            [
                first,
                second with
                {
                    Edits = [first.Edits[0] with { Replacement = "conflict" }, .. second.Edits],
                },
                independent,
            ],
            Contexts =
            [
                context with
                {
                    Findings =
                    [
                        .. context.Findings,
                        context.Findings[1] with
                        {
                            Start = 9,
                            Length = 4,
                            BatchId = "independent",
                        },
                    ],
                },
            ],
        };

        var result = await fixture.Applier.ApplyAsync(
            fixture.Snapshot,
            Encoding.UTF8.GetString(BundleResponseProtocol.Serialize(response)),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(FixApplicationOutcome.Completed, result.Outcome);
        Assert.Equal([fixture.Paths[1]], result.Changed);
        Assert.Equal([fixture.Documents[0].Text, "😀alpha\r\nB\ngamma\r"], fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Theory]
    [InlineData(-1, 5, "alpha")]
    [InlineData(2, int.MaxValue, "alpha")]
    [InlineData(2, 5, "stale")]
    public async Task Invalid_later_edit_rejects_the_entire_plan_before_any_write(
        int start,
        int length,
        string original
    )
    {
        var fixture = new FixFixture();
        var second = fixture.Response.Batches[1];
        var response = fixture.Response with
        {
            Batches =
            [
                fixture.Response.Batches[0],
                second with
                {
                    Edits =
                    [
                        second.Edits[0] with
                        {
                            Start = start,
                            Length = length,
                            OriginalText = original,
                        },
                    ],
                },
            ],
        };

        var result = await fixture.Applier.ApplyAsync(
            fixture.Snapshot,
            Encoding.UTF8.GetString(BundleResponseProtocol.Serialize(response)),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(FixApplicationOutcome.PreparationFailed, result.Outcome);
        Assert.Empty(result.Changed);
        Assert.Equal(fixture.Documents.Select(document => document.Text), fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Fact]
    public async Task Replacing_source_with_identical_bytes_under_a_new_identity_is_rejected()
    {
        var fixture = new FixFixture();
        fixture.Replacer.OnReplacement[fixture.Paths[0]] = () =>
            fixture.Probe.Overrides[fixture.Paths[0]] = new("new inode", 1, true);

        var result = await fixture.Applier.ApplyAsync(
            fixture.Snapshot,
            fixture.Json,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(FixApplicationOutcome.CommitFailed, result.Outcome);
        Assert.Empty(result.Changed);
        Assert.Equal(fixture.Documents.Select(document => document.Text), fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Fact]
    public async Task Cancellation_between_files_retains_completed_replacements()
    {
        var fixture = new FixFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Replacer.OnReplacement[fixture.Paths[1]] = cancellation.Cancel;

        var result = await fixture.Applier.ApplyAsync(
            fixture.Snapshot,
            fixture.Json,
            cancellation.Token
        );

        Assert.Equal(FixApplicationOutcome.Cancelled, result.Outcome);
        Assert.Equal([fixture.Paths[0]], result.Changed);
        Assert.Equal(fixture.Paths[1], result.Failed);
        Assert.Equal(["😀é\r\nbeta\ngamma\r", fixture.Documents[1].Text], fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }
}
