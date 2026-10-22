using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Semantics;

public sealed class DeclarationFactsTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void A_symbol_descriptor_keeps_nested_metadata_and_constructed_type_identity()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "App",
            [
                new(
                    "A.cs",
                    "namespace A; class Outer<T> { public class Inner<U> {} } class Use { Outer<int>.Inner<string>? value; }"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var type = (INamedTypeSymbol)Code.Fields.Named("value").In(solution).Single().Type!;

        var descriptor = CodeType.FromSymbol(type);

        Assert.Equal(
            ("A.Outer`1+Inner`1", "App", true, false),
            (
                descriptor.MetadataName,
                descriptor.AssemblyName,
                descriptor.Matches(type),
                descriptor.Matches(type.OriginalDefinition)
            )
        );
    }

    [Fact]
    public void Declaration_shapes_and_name_prefiltered_uses_are_separate_facts()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "App",
            [
                new(
                    "Data.cs",
                    "namespace App; class Data { public int Value { get; set; } } class Helper { public static int Count() => 1; }"
                ),
                new(
                    "Use.cs",
                    "class Use { App.Data? value; System.Collections.Generic.List<App.Data>? items; }"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var types = Code.Types.In(solution);
        var data = types.Single(type => type.Name == "Data");
        var facts = types
            .Select(type =>
                (
                    type.Name,
                    DeclarationFacts.IsDataClass((BaseTypeDeclarationSyntax)type.Syntax),
                    DeclarationFacts.HasOnlyStaticMembers(type),
                    DeclarationFacts.IsTopLevelProgram(type)
                )
            )
            .ToArray();

        Assert.Equal(
            new[]
            {
                ("Data", true, false, false),
                ("Helper", false, true, false),
                ("Use", true, false, false),
            },
            facts
        );
        Assert.Equal(
            ["Data", "Data"],
            DeclarationFacts
                .UsesOf(solution, data.Symbol)
                .Select(use => use.Name.ToString())
                .ToArray()
        );
        Assert.True(DeclarationFacts.IsUsedOutside(data.Symbol, data.Source));
        Assert.True(DeclarationFacts.IsTypeArgument(solution, data.Symbol));
        Assert.Equal("global::App.Data", DeclarationFacts.Key(data.Symbol));
        Assert.Equal("App", DeclarationFacts.NamespaceFromFolders(data.Source));
        Assert.Equal("Data.cs", DeclarationFacts.RelativePath(data.Source));
        Assert.Equal(
            ["Data", "Helper"],
            DeclarationFacts
                .TopLevelTypes(data.Source)
                .Select(type => type.Identifier.ValueText)
                .ToArray()
        );
    }
}
