using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Semantics;

public sealed class CodeTypeTests(SemanticRuleFixture fixture) : IClassFixture<SemanticRuleFixture>
{
    [Theory]
    [InlineData("+")]
    [InlineData(".")]
    public void Open_generic_names_match_constructed_symbols_nested_types_and_array_parameters(
        string separator
    )
    {
        var project = fixture.Project(
            """
            namespace Product;
            class Outer<T> { public class Inner<U, V> { } }
            class Consumer
            {
                Outer<int>.Inner<string, bool> Nested;
                System.Collections.Generic.List<string>[] Names;
                System.Collections.Generic.List<int>[] Numbers;
                void Save(System.Collections.Generic.List<string>[] values) { }
            }
            """,
            "Product"
        );
        var consumer = project.Compilation.GetTypeByMetadataName("Product.Consumer")!;
        var nested = ((IFieldSymbol)consumer.GetMembers("Nested").Single()).Type;
        var names = ((IFieldSymbol)consumer.GetMembers("Names").Single()).Type;
        var numbers = ((IFieldSymbol)consumer.GetMembers("Numbers").Single()).Type;
        var save = (IMethodSymbol)consumer.GetMembers("Save").Single();
        var arrays = CodeType.Named("System.Collections.Generic.List<>[]");

        var matches = new[]
        {
            CodeType.Named($"Product.Outer<>{separator}Inner<,>", "Product").Matches(nested),
            CodeType.Named($"Product.Outer<>{separator}Inner<>", "Product").Matches(nested),
            CodeType.Named($"Product.Outer<>{separator}Inner<,>", "Other").Matches(nested),
            arrays.Matches(names),
            arrays.Matches(numbers),
            CodeType.Named("Product.Consumer").Member("Save").WithParameters(arrays).Matches(save),
        };

        Assert.Equal([true, false, false, true, true, true], matches);
    }

    [Fact]
    public void Dotted_nested_names_match_deeply_nested_types_arrays_and_members()
    {
        var project = fixture.Project(
            """
            namespace Product;
            class Outer<T> { public class Middle { public class Inner<U, V> { } } }
            class Consumer
            {
                Outer<int>.Middle.Inner<string, bool>[][] Values;
                void Save(Outer<int>.Middle.Inner<string, bool>[][] values) { }
            }
            """,
            "Product"
        );
        var consumer = project.Compilation.GetTypeByMetadataName("Product.Consumer")!;
        var values = ((IFieldSymbol)consumer.GetMembers("Values").Single()).Type;
        var save = (IMethodSymbol)consumer.GetMembers("Save").Single();
        var arrays = CodeType.Named("Product.Outer<>.Middle.Inner<,>[][]");

        var matches = new[]
        {
            arrays.Matches(values),
            CodeType.Named("Product.Outer<>.Middle.Inner<,>[][,]").Matches(values),
            CodeType.Named("Product.Consumer").Member("Save").WithParameters(arrays).Matches(save),
        };

        Assert.Equal([true, false, true], matches);
    }

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
