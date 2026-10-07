using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Analysis;

public sealed class DeclarationElementsTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    private const string Source = """
        using System;
        using System.Threading;
        namespace Shop
        {
            [Obsolete] public partial class Order
            {
                private const int Limit = 3;
                private static readonly string _prefix = "x", _suffix;
                public int count;
                public string Name { get; init; } = "";
                public required int Size { get; set; }
                public int Total => count * 2;
                internal int Computed { get { return count; } }
                public void Submit(int quantity, CancellationToken token = default) { }
                private static int Parse(string text) => text.Length;
                public void Run(params int[] values) { Func<int, int> twice = x => x * 2; }
            }
            partial class Order { }
            internal interface IOrder { }
            public record struct Line(int Number);
        }
        """;

    [Fact]
    public void Fields_are_written_variables_with_their_shared_modifiers()
    {
        var solution = Analyze();

        var fields = Code
            .Fields.In(solution)
            .Select(field =>
                $"{field.Name}:{field.Accessibility}:{field.IsConst}:{field.IsStatic}:{field.IsReadOnly}:{field.TypeIs<string>()}:{field.Initializer?.Syntax}"
            )
            .ToArray();

        Assert.Equal(
            [
                "Limit:Private:True:True:False:False:3",
                "_prefix:Private:False:True:True:True:\"x\"",
                "_suffix:Private:False:True:True:True:",
                "count:Public:False:False:False:False:",
            ],
            fields
        );
    }

    [Fact]
    public void Properties_describe_accessors_and_storage()
    {
        var solution = Analyze();

        var properties = Code
            .Properties.In(solution)
            .Select(property =>
                $"{property.Name}:{property.HasGetter}:{property.HasSetter}:{property.HasInit}:{property.IsAutoProperty}:{property.HasExplicitModifier(Modifier.Required)}:{property.Initializer?.Syntax}"
            )
            .ToArray();

        Assert.Equal(
            [
                "Name:True:False:True:True:False:\"\"",
                "Size:True:True:False:True:True:",
                "Total:True:False:False:False:False:",
                "Computed:True:False:False:False:False:",
            ],
            properties
        );
    }

    [Fact]
    public void Parameters_cover_methods_lambdas_and_primary_constructors()
    {
        var solution = Analyze();

        var parameters = Code
            .Parameters.In(solution)
            .Select(parameter =>
                $"{parameter.Name}:{parameter.Ordinal}:{parameter.TypeIs<CancellationToken>()}:{parameter.HasDefaultValue}:{parameter.HasExplicitModifier(Modifier.Params)}:{parameter.IsLambdaParameter}:{parameter.ContainingSymbol?.Name}"
            )
            .ToArray();

        Assert.Equal(
            [
                "quantity:0:False:False:False:False:Submit",
                "token:1:True:True:False:False:Submit",
                "text:0:False:False:False:False:Parse",
                "values:0:False:False:True:False:Run",
                "x:0:False:False:False:True:",
                "Number:0:False:False:False:False:.ctor",
            ],
            parameters
        );
    }

    [Fact]
    public void Methods_expose_signature_body_and_accessibility_facts()
    {
        var solution = Analyze();

        var methods = Code
            .Methods.In(solution)
            .Select(method =>
                $"{method.Name}:{method.Accessibility}:{method.IsStatic}:{method.ReturnsVoid}:{method.ReturnTypeIs(CodeType.Of<int>())}:{method.Parameters.Count}:{method.Body()?.Calls().Count()}"
            )
            .ToArray();

        Assert.Equal(
            [
                "Submit:Public:False:True:False:2:0",
                "Parse:Private:True:False:True:1:0",
                "Run:Public:False:True:False:1:0",
            ],
            methods
        );
    }

    [Fact]
    public void Type_definitions_combine_partial_modifiers_and_describe_their_kind()
    {
        var solution = Analyze();

        var types = Code
            .Types.In(solution)
            .Select(type =>
                $"{type.Name}:{type.Accessibility}:{type.IsClass}:{type.IsInterface}:{type.IsStruct}:{type.IsRecord}:{type.HasExplicitModifier(Modifier.Public)}:{type.HasAttribute(CodeType.Of<ObsoleteAttribute>())}"
            )
            .ToArray();

        Assert.Equal(
            [
                "Order:Public:True:False:False:False:True:True",
                "IOrder:Internal:False:True:False:False:False:False",
                "Line:Public:False:False:True:True:True:False",
            ],
            types
        );
    }

    [Fact]
    public void Shared_declaration_filters_apply_to_every_declaration_kind()
    {
        var solution = Analyze();

        var privateStatic = Code
            .Fields.WithAccessibility(Accessibility.Private)
            .WithExplicitModifier(Modifier.Static)
            .NameMatching("_*")
            .In(solution)
            .Select(field => field.Name);
        var named = Code.Methods.Named("Run", "Parse").In(solution).Select(method => method.Name);
        var attributed = Code
            .TypeDeclarations.WithAttribute(CodeType.Of<ObsoleteAttribute>())
            .In(solution)
            .Select(type => type.Location.Line);
        var words = Code
            .Properties.WithNameContainingAnyWord(
                ["total", "size"],
                StringComparison.OrdinalIgnoreCase
            )
            .In(solution)
            .Select(property => property.Name);
        var prefixed = Code
            .Parameters.Where(parameter =>
                parameter.NameStartsWith("t") && parameter.NameEndsWith("n")
            )
            .In(solution)
            .Select(parameter => parameter.Name);

        Assert.Equal(["_prefix", "_suffix"], privateStatic);
        Assert.Equal(["Parse", "Run"], named);
        Assert.Equal([5], attributed);
        Assert.Equal(["Size", "Total"], words);
        Assert.Equal(["token"], prefixed);
    }

    [Fact]
    public async Task Documentation_rules_can_require_comments_on_public_declarations()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "Api.cs",
                    "/// <summary>Documented.</summary>\npublic partial class Api\n{\n    /// <summary>Documented.</summary>\n    public int Count;\n    public int Missing() => 0;\n    private int Hidden() => 0;\n}\npartial class Api { }\npublic interface IUndocumented { }\n"
                ),
            ]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("DOC001", "Document every public API.")
            .For(Code.Types.WithAccessibility(Accessibility.Public))
            .Require(type => type.HasDocumentationComment())
            .For(Code.Methods.WithAccessibility(Accessibility.Public))
            .Require(method => method.HasDocumentationComment())
            .For(Code.Fields.WithAccessibility(Accessibility.Public))
            .Require(field => field.HasDocumentationComment());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            DOC001 Document every public API.
            Api.cs
              6:16
              10:18

            """.Replace("\r\n", "\n"),
            result.Output
        );
    }

    [Fact]
    public async Task Findings_can_report_at_an_explicit_modifier()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "Types.cs",
                    "internal sealed class Hidden { }\npublic class Shown { private int _count; }\n"
                ),
            ]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("MOD001", "Omit default modifiers.")
            .For(Code.TypeDeclarations.TopLevel().WithExplicitModifier(Modifier.Internal))
            .ReportAt(type => type.ExplicitModifier(Modifier.Internal))
            .Forbid()
            .For(Code.Fields.WithExplicitModifier(Modifier.Private))
            .ReportAt(field => field.ExplicitModifier(Modifier.Private))
            .Forbid();

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            MOD001 Omit default modifiers.
            Types.cs
              1
              2:22

            """.Replace("\r\n", "\n"),
            result.Output
        );
    }

    private AnalysisSolution Analyze()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Shop", [new("Order.cs", Source)]);
        return workspace.Analyze(TestContext.Current.CancellationToken);
    }
}
