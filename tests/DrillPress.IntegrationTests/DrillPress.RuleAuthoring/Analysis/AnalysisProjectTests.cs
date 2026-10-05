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
}
