using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Manifest;

public sealed class FixPlanApplierMoveTests
{
    [Fact]
    public async Task Files_move_into_new_folders_after_their_edits()
    {
        var fixture = new MoveFixture();
        var response = fixture.Response(
            fixture.Batch("rename", [], fixture.Move(0, "Invoice.cs")),
            fixture.Batch("mirror", [fixture.Edit(1)], fixture.Move(1, "Services", "B.cs"))
        );

        var result = await fixture.Applier.ApplyAsync(
            fixture.Snapshot,
            fixture.Json(response),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(FixApplicationOutcome.Completed, result.Outcome);
        Assert.Equal(
            [fixture.Path("Invoice.cs"), fixture.Path("Services", "B.cs")],
            result.Changed
        );
        Assert.Equal(
            [
                new MovedFile(fixture.Documents[0].Path, fixture.Path("Invoice.cs")),
                new MovedFile(fixture.Documents[1].Path, fixture.Path("Services", "B.cs")),
            ],
            result.Moved
        );
        Assert.Equal(["Invoice.cs", "Orders.csproj", "Services/B.cs"], fixture.Files());
        Assert.Equal(
            [MoveFixture.Text, "class A { string Value = \"\"; }\n"],
            [
                fixture.FileSystem.File.ReadAllText(fixture.Path("Invoice.cs")),
                fixture.FileSystem.File.ReadAllText(fixture.Path("Services", "B.cs")),
            ]
        );
    }

    [Fact]
    public async Task A_destination_created_after_analysis_stops_before_any_write()
    {
        var fixture = new MoveFixture();
        fixture.FileSystem.AddFile(fixture.Path("Invoice.cs"), "class Other {}");
        var response = fixture.Response(
            fixture.Batch("edit", [fixture.Edit(0)]),
            fixture.Batch("rename", [], fixture.Move(1, "Invoice.cs"))
        );

        var result = await fixture.Applier.ApplyAsync(
            fixture.Snapshot,
            fixture.Json(response),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(FixApplicationOutcome.PreparationFailed, result.Outcome);
        Assert.Equal([], result.Changed);
        Assert.Equal(fixture.Documents[1].Path, result.Failed);
        Assert.Equal([fixture.Documents[0].Path], result.Pending);
        Assert.Equal(
            $"Cannot move to '{fixture.Path("Invoice.cs")}': the file already exists.",
            result.Error
        );
        Assert.Equal<MovedFile>([], result.Moved);
        Assert.Equal(["A.cs", "B.cs", "Invoice.cs", "Orders.csproj"], fixture.Files());
        Assert.Equal(
            MoveFixture.Text,
            fixture.FileSystem.File.ReadAllText(fixture.Documents[0].Path)
        );
    }

    [Fact]
    public async Task A_folder_holding_another_project_is_not_entered()
    {
        var fixture = new MoveFixture();
        fixture.FileSystem.AddFile(fixture.Path("Nested", "Nested.csproj"), "<Project />");
        var response = fixture.Response(
            fixture.Batch("move", [], fixture.Move(0, "Nested", "A.cs"))
        );

        var result = await fixture.Applier.ApplyAsync(
            fixture.Snapshot,
            fixture.Json(response),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(FixApplicationOutcome.PreparationFailed, result.Outcome);
        Assert.Equal(
            $"Cannot move to '{fixture.Path("Nested", "A.cs")}': '{fixture.Path("Nested")}' contains another project.",
            result.Error
        );
        Assert.Equal(["A.cs", "B.cs", "Nested/Nested.csproj", "Orders.csproj"], fixture.Files());
    }

    [Fact]
    public async Task A_source_changed_before_its_move_keeps_earlier_moves_for_recovery()
    {
        var fixture = new MoveFixture();
        fixture.Replacer.OnMove[fixture.Documents[1].Path] = () =>
            fixture.FileSystem.File.WriteAllText(fixture.Documents[1].Path, "external edit");
        var response = fixture.Response(
            fixture.Batch("first", [], fixture.Move(0, "First.cs")),
            fixture.Batch("second", [], fixture.Move(1, "Second.cs"))
        );

        var result = await fixture.Applier.ApplyAsync(
            fixture.Snapshot,
            fixture.Json(response),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(FixApplicationOutcome.CommitFailed, result.Outcome);
        Assert.Equal([fixture.Path("First.cs")], result.Changed);
        Assert.Equal(fixture.Documents[1].Path, result.Failed);
        Assert.Equal([], result.Pending);
        Assert.Equal("Source identity or bytes changed before the move.", result.Error);
        Assert.Equal<MovedFile>(
            [new(fixture.Documents[0].Path, fixture.Path("First.cs"))],
            result.Moved
        );
        Assert.Equal(["B.cs", "First.cs", "Orders.csproj"], fixture.Files());
    }

    [Fact]
    public async Task An_edited_file_whose_move_fails_is_reported_as_changed_and_failed()
    {
        var fixture = new MoveFixture();
        fixture.Replacer.OnMove[fixture.Documents[0].Path] = () =>
            fixture.FileSystem.File.WriteAllText(fixture.Documents[0].Path, "external edit");
        var response = fixture.Response(
            fixture.Batch("move", [fixture.Edit(0)], fixture.Move(0, "Invoice.cs"))
        );

        var result = await fixture.Applier.ApplyAsync(
            fixture.Snapshot,
            fixture.Json(response),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(FixApplicationOutcome.CommitFailed, result.Outcome);
        Assert.Equal([fixture.Documents[0].Path], result.Changed);
        Assert.Equal(fixture.Documents[0].Path, result.Failed);
        Assert.Equal([], result.Pending);
        Assert.Equal<MovedFile>([], result.Moved);
        Assert.Equal(["A.cs", "B.cs", "Orders.csproj"], fixture.Files());
    }
}
