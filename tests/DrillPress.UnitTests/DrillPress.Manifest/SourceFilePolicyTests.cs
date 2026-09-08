using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Manifest;

public sealed class SourceFilePolicyTests
{
    [Fact]
    public void Existing_generated_path_alias_also_withholds_the_ordinary_source()
    {
        var fixture = new FixFixture();
        fixture.Probe.Overrides[fixture.Paths[1]] = new(fixture.Paths[0], 1, true);
        var project = fixture.Snapshot.Projects[0];
        var snapshot = fixture.Snapshot with { Projects = [project with
        {
            Documents = [project.Documents[0], project.Documents[1] with { IsGenerated = true, IsEditable = false }],
        }] };
        var policy = new SourceFilePolicy(fixture.FileSystem, fixture.Probe);

        var result = policy.Restrict(snapshot, TestContext.Current.CancellationToken);

        Assert.Equal([false, false], result.Projects[0].Documents.Select(document => document.IsEditable));
    }

    [Fact]
    public void Distinct_loaded_paths_to_one_physical_file_are_both_ineligible()
    {
        var fixture = new FixFixture();
        fixture.Probe.Overrides[fixture.Paths[1]] = new(fixture.Paths[0], 1, true);
        var policy = new SourceFilePolicy(fixture.FileSystem, fixture.Probe);

        var result = policy.Restrict(fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal([false, false], result.Projects[0].Documents.Select(document => document.IsEditable));
        Assert.Equal(fixture.Documents.Select(document => document.FileIdentity), result.Projects[0].Documents.Select(document => document.FileIdentity));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(1, false)]
    public void Hard_links_and_non_regular_files_are_ineligible(uint links, bool regular)
    {
        var fixture = new FixFixture();
        fixture.Probe.Overrides[fixture.Paths[0]] = new("physical", links, regular);
        var policy = new SourceFilePolicy(fixture.FileSystem, fixture.Probe);

        var result = policy.Restrict(fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal([false, true], result.Projects[0].Documents.Select(document => document.IsEditable));
    }

    [Fact]
    public void Unknown_identity_withholds_all_edits_in_case_it_aliases_another_source()
    {
        var fixture = new FixFixture();
        fixture.Probe.Overrides[fixture.Paths[0]] = null;
        var policy = new SourceFilePolicy(fixture.FileSystem, fixture.Probe);

        var result = policy.Restrict(fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal([false, false], result.Projects[0].Documents.Select(document => document.IsEditable));
    }

    [Fact]
    public void Read_only_source_remains_available_for_analysis_without_edits()
    {
        var fixture = new FixFixture();
        fixture.FileSystem.File.SetAttributes(fixture.Paths[0], FileAttributes.ReadOnly);
        var policy = new SourceFilePolicy(fixture.FileSystem, fixture.Probe);

        var result = policy.Restrict(fixture.Snapshot, TestContext.Current.CancellationToken);

        Assert.Equal([false, true], result.Projects[0].Documents.Select(document => document.IsEditable));
        Assert.Equal(fixture.Documents.Select(document => document.Text), result.Projects[0].Documents.Select(document => document.Text));
    }
}
