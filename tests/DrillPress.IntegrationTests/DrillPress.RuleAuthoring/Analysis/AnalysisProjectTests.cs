using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Analysis;

public sealed class AnalysisProjectTests(SemanticRuleFixture fixture)
    : IClassFixture<SemanticRuleFixture>
{
    [Fact]
    public void Resolves_constructed_runtime_types_and_arrays_in_the_evaluated_framework()
    {
        var project = fixture.Project(
            "class C { System.Collections.Generic.List<string>[,] Values; }"
        );
        var descriptor = CodeType.Of<List<string>[,]>();
        var expected = (
            (IFieldSymbol)
                project.Compilation.GetTypeByMetadataName("C")!.GetMembers("Values").Single()
        ).Type;

        var availability = project.InspectType(descriptor);

        Assert.Equal(TypeAvailabilityStatus.Available, availability.Status);
        Assert.True(SymbolEqualityComparer.Default.Equals(expected, availability.Type));
        Assert.True(project.HasType(CodeType.Framework("System.Collections.Generic.List<>")));
    }

    [Fact]
    public void Candidates_reach_the_latest_analysis_that_includes_their_project()
    {
        var project = fixture.Project("class C { void M() => M(); }");
        var names = new AnalysisFact<string[]>(solution =>
            solution.Projects.Select(project => project.Name).ToArray()
        );
        var call = Code.Calls.In(new AnalysisSolution([project])).Single();

        var latest = new AnalysisSolution([project]);

        Assert.Same(latest, project.Solution);
        Assert.Same(latest, call.Source.Solution);
        Assert.Equal(["Test"], names.In(call.Source.Solution));
    }

    [Fact]
    public void A_project_outside_any_analysis_has_no_solution()
    {
        var project = fixture.Project("class C { }");

        var error = Assert.Throws<InvalidOperationException>(() => project.Solution);

        Assert.Equal("The project is not part of an analysis yet.", error.Message);
    }
}
