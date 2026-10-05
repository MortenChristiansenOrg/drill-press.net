using System.IO.Abstractions.TestingHelpers;
using DrillPress.UnitTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Analysis;

public sealed class AnalysisProjectTests
{
    [Fact]
    public void Resolves_qualified_identities_and_preserves_ambiguity_and_aliases()
    {
        var first = Reference("First");
        var second = Reference("Second").WithAliases(["other"]);
        var project = Project([first, second]);
        var identity = CodeType.Named("Contract.Marker<>", "Second");

        var available = project.InspectType(identity);
        var ambiguous = project.InspectType(CodeType.Named("Contract.Marker<>"));
        var missing = project.InspectType(CodeType.Named("Contract.Marker<>", "Missing"));

        Assert.Equal(TypeAvailabilityStatus.Available, available.Status);
        Assert.Equal("Second", available.Type!.ContainingAssembly.Name);
        Assert.Same(available, project.InspectType(identity));
        Assert.Equal(new TypeAvailability(TypeAvailabilityStatus.Ambiguous, null), ambiguous);
        Assert.Equal(new TypeAvailability(TypeAvailabilityStatus.Missing, null), missing);
        Assert.False(project.HasType(CodeType.Named("Contract.Marker<>")));
        Assert.True(project.HasType(identity));
    }

    [Fact]
    public void Scopes_all_source_models_by_context_and_evaluated_test_classification()
    {
        var production = Project([Reference("Contract")]);
        var tests = Project([Reference("Contract")], true);
        var unrelated = Project([]);
        var solution = new AnalysisSolution(
            [production, tests, unrelated],
            TestContext.Current.CancellationToken
        );

        var files = Code
            .Files.InNonTestProjects()
            .InProjectsWithType(CodeType.Named("Contract.Marker<>", "Contract"))
            .In(solution);

        Assert.Same(production, Assert.Single(files).Source.Project);
        Assert.False(unrelated.HasType(CodeType.Named("Contract.Marker<>")));
        Assert.False(production.HasType(CodeType.Framework("Contract.Marker<>")));
    }

    private static CompilationReference Reference(string assembly) =>
        CSharpCompilation
            .Create(
                assembly,
                [CSharpSyntaxTree.ParseText("namespace Contract { public class Marker<T> {} }")]
            )
            .ToMetadataReference();

    private static AnalysisProject Project(MetadataReference[] references, bool isTest = false)
    {
        var snapshot = TestSnapshots.CreateProject("/scope/Target.cs", "class Target {}") with
        {
            IsTestProject = isTest,
        };
        var compilation = CSharpCompilation.Create(
            "Target",
            [
                CSharpSyntaxTree.ParseText(
                    snapshot.Documents[0].Text,
                    path: snapshot.Documents[0].Path
                ),
            ],
            references
        );
        return new(
            snapshot,
            compilation,
            new MockFileSystem(),
            TestContext.Current.CancellationToken
        );
    }
}
