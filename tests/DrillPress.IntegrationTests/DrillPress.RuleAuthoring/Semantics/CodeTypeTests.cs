using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Semantics;

public sealed class CodeTypeTests(SemanticRuleFixture fixture) : IClassFixture<SemanticRuleFixture>
{
    [Fact]
    public void Constructed_generics_match_arguments_and_framework_facades()
    {
        var project = fixture.Project(
            "class C { System.Collections.Generic.List<string> Names = new(); System.Collections.Generic.List<int> Numbers = new(); }"
        );
        var type = project.Compilation.GetTypeByMetadataName("C")!;
        var names = (INamedTypeSymbol)((IFieldSymbol)type.GetMembers("Names").Single()).Type;
        var numbers = (INamedTypeSymbol)((IFieldSymbol)type.GetMembers("Numbers").Single()).Type;
        var identity = CodeType.Of<List<string>>();

        var matches = new[]
        {
            identity.Matches(names),
            identity.Matches(numbers),
            CodeType.Of<Dictionary<string, int>>().Matches(names),
        };

        Assert.Equal([true, false, false], matches);
    }

    [Fact]
    public void Named_identity_can_restrict_the_declaring_assembly()
    {
        var project = fixture.Project("namespace Target { class Value { } }", "Product");
        var symbol = project.Compilation.GetTypeByMetadataName("Target.Value")!;

        var matches = new[]
        {
            CodeType.Named("Target.Value").Matches(symbol),
            CodeType.Named("Target.Value", "Product").Matches(symbol),
            CodeType.Named("Target.Value", "Other").Matches(symbol),
        };

        Assert.Equal([true, true, false], matches);
    }

    [Fact]
    public void Nested_constructed_BCL_types_retain_outer_arguments()
    {
        var project = fixture.Project(
            "class C { System.Collections.Generic.List<string>.Enumerator Value; }"
        );
        var symbol = (INamedTypeSymbol)
            (
                (IFieldSymbol)
                    project.Compilation.GetTypeByMetadataName("C")!.GetMembers("Value").Single()
            ).Type;

        var matches = new[]
        {
            CodeType.Of<List<string>.Enumerator>().Matches(symbol),
            CodeType.Of<List<int>.Enumerator>().Matches(symbol),
            CodeType
                .Named("System.Collections.Generic.List`1+Enumerator", "System.Unrelated")
                .Matches(symbol),
        };

        Assert.Equal([true, false, false], matches);
    }

    [Fact]
    public void Type_query_includes_delegates_and_deduplicates_partial_types()
    {
        var project = fixture.Project(
            "delegate void D(); enum E { Value } partial class C { } partial class C { } record R;"
        );
        var rules = new RuleSet();
        rules.For(Code.Types).Forbid("TYPE001", "Review type.");

        var diagnostics = rules.Evaluate(new AnalysisSolution([project]));

        Assert.Equal(
            ["D", "E", "C", "R"],
            diagnostics.Select(diagnostic =>
                project
                    .Snapshot.Documents[0]
                    .Text.Substring(diagnostic.Location.Start, diagnostic.Location.Length)
            )
        );
    }
}
