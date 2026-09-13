using DrillPress.Baselines;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Queries;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Baselines;

public sealed class SourceBaselineTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Accepted_source_tracks_added_changed_and_unchanged_files_and_previous_contracts()
    {
        var previous = fixture.Workspace();
        previous.AddProject(
            "Library",
            [new("A.cs", "internal class A { }"), new("B.cs", "class B { }")]
        );
        var baseline = new SourceBaseline(previous.Analyze(TestContext.Current.CancellationToken));
        var current = fixture.Workspace();
        current.AddProject(
            "Library",
            [
                new("A.cs", "public class A { }"),
                new("B.cs", "class B { }"),
                new("C.cs", "class C { }"),
            ]
        );
        var solution = current.Analyze(TestContext.Current.CancellationToken);

        var changes = Sources
            .Files.In(solution)
            .Select(file => (file.Name, baseline.ChangeOf(file)))
            .ToArray();
        var type = solution.Types.Single(type => type.Symbol.Name == "A");
        var accessibility = baseline.PreviousAccessibility(type.Source.Project, type.Symbol);

        Assert.Equal(
            [
                ("A.cs", SourceChange.Modified),
                ("B.cs", SourceChange.Unchanged),
                ("C.cs", SourceChange.Added),
            ],
            changes
        );
        Assert.Equal(Accessibility.Internal, accessibility);
        Assert.Equal(SourceChange.Modified, baseline.ChangeOf(type.Source.Project, type.Symbol));
        Assert.Equal(
            ["A.cs", "C.cs"],
            baseline.ChangedFiles.In(solution).Select(file => file.Name)
        );
    }
}
