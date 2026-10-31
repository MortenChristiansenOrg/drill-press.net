using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class PropertyViewTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    private static readonly CodeType _required = CodeType.Named("RequiredAttribute");

    [Fact]
    public void Explicit_and_positional_properties_share_one_view()
    {
        var solution = Analyze(
            """
            using System;
            #nullable enable
            class RequiredAttribute : Attribute {}
            class Input {
                [Required] public required string Name { get; init; }
                public string[] Tags { get; init; } = [];
            }
            record RecordInput([property: Required] string Name, string Label = "", Guid Id = default, int? Count = null);
            class H {
                void Ok(object value) {}
                void M() { Ok(new Input { Name = "" }); Ok(new RecordInput("")); }
            }
            """
        );

        var views = Views().In(solution);

        Assert.Equal(
            [
                "Input.Name string PropertyDeclaration @5 NonNullable required",
                "Input.Tags string[] PropertyDeclaration @6 NonNullable PropertyInitializer:[]",
                "RecordInput.Name string PositionalParameter @8 NonNullable required",
                "RecordInput.Label string PositionalParameter @8 NonNullable PositionalParameterDefault:\"\"",
                "RecordInput.Id System.Guid PositionalParameter @8 NonNullable PositionalParameterDefault:default",
                "RecordInput.Count int? PositionalParameter @8 Nullable PositionalParameterDefault:null",
            ],
            views.Select(Describe)
        );
        Assert.All(
            views.Where(view => view.Origin == PropertyViewOrigin.PositionalParameter),
            view => Assert.Equal(view.Name, view.PositionalParameter!.Name)
        );
        Assert.Equal(
            ["Name", "Tags"],
            views
                .Where(view => view.Origin == PropertyViewOrigin.PropertyDeclaration)
                .Select(view => view.Declaration!.Name)
        );
    }

    [Fact]
    public void Positional_parameter_location_is_the_identifier_and_the_default_has_evidence()
    {
        var solution = Analyze(
            """
            #nullable enable
            record RecordInput(string Label = "");
            class H { void Ok(object value) {} void M() { Ok(new RecordInput()); } }
            """
        );

        var view = Views().In(solution).Single();

        Assert.Equal(2, view.Location.Line);
        Assert.Equal("Label".Length, view.Location.Length);
        Assert.Equal(27, view.Location.Column);
        Assert.Equal("Inputs.cs", Path.GetFileName(view.Source.Document.Path));
        Assert.True(view.Default!.Value.ResultValues().IsNonNullByLanguage);
    }

    [Fact]
    public void Redeclared_positional_property_is_reported_once_at_its_declaration()
    {
        var solution = Analyze(
            """
            #nullable enable
            record Input(string Name = "parameter") {
                public string Name { get; init; } = "initializer";
            }
            class H { void Ok(object value) {} void M() { Ok(new Input()); } }
            """
        );

        var views = Views().In(solution);

        Assert.Equal(
            [
                "Input.Name string PropertyDeclaration @3 NonNullable PropertyInitializer:\"initializer\"",
            ],
            views.Select(Describe)
        );
    }

    [Fact]
    public void Inherited_positional_member_maps_to_the_base_declaration()
    {
        var solution = Analyze(
            """
            #nullable enable
            record Base(string Name);
            record Derived(string Name, int Age) : Base(Name);
            class H { void Ok(object value) {} void M() { Ok(new Derived("", 1)); } }
            """
        );

        var views = Views().In(solution);

        Assert.Equal(
            [
                "Base.Name string PositionalParameter @2 NonNullable",
                "Derived.Age int PositionalParameter @3 NonNullable",
            ],
            views.Select(Describe)
        );
    }

    [Fact]
    public void Inherited_properties_keep_their_owner_and_yield_to_overrides_and_hiding()
    {
        var solution = Analyze(
            """
            #nullable enable
            class Base {
                public string Inherited { get; init; } = "";
                public virtual string Overridden { get; init; } = "";
                public string Hidden { get; init; } = "";
            }
            class Derived : Base {
                public override string Overridden { get; init; } = "";
                public new string? Hidden { get; init; }
            }
            class H { void Ok(object value) {} void M() { Ok(new Derived()); } }
            """
        );

        var views = Views().In(solution);

        Assert.Equal(
            [
                "Base.Inherited string PropertyDeclaration @3 NonNullable PropertyInitializer:\"\"",
                "Derived.Overridden string PropertyDeclaration @8 NonNullable PropertyInitializer:\"\"",
                "Derived.Hidden string? PropertyDeclaration @9 Nullable",
            ],
            views.Select(Describe)
        );
    }

    [Fact]
    public void Distinct_generic_substitutions_stay_separate_and_identical_ones_merge()
    {
        var solution = Analyze(
            """
            #nullable enable
            class Envelope<T> { public T? Value { get; init; } }
            class H {
                void Ok(object value) {}
                void M() {
                    Ok(new Envelope<string>());
                    Ok(new Envelope<string>());
                    Ok(new Envelope<int>());
                    Ok(new Envelope<int?>());
                }
            }
            """
        );

        var views = Views().In(solution);

        Assert.Equal(
            [
                "Envelope<int>.Value int PropertyDeclaration @2 NonNullable",
                "Envelope<int?>.Value int? PropertyDeclaration @2 Nullable",
                "Envelope<string>.Value string? PropertyDeclaration @2 Nullable",
            ],
            views.Select(Describe).Order(StringComparer.Ordinal)
        );
        Assert.Equal([1, 1, 2], views.Select(view => view.ReachedFrom.Count).Order());
        Assert.All(views, view => Assert.True(view.IsConstructed));
        Assert.All(
            views,
            view =>
                Assert.Equal(
                    "T?",
                    view.Definition.Type.ToDisplayString(
                        SymbolDisplayFormat.MinimallyQualifiedFormat.WithMiscellaneousOptions(
                            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                        )
                    )
                )
        );
    }

    [Fact]
    public void Nullable_substitutions_reached_from_one_seed_stay_separate()
    {
        var solution = Analyze(
            """
            #nullable enable
            class Envelope<T> { public T Value { get; init; } = default!; }
            class Root {
                public Envelope<string> Sure { get; init; } = new();
                public Envelope<string?> Maybe { get; init; } = new();
            }
            class H { void Ok(object value) {} void M() { Ok(new Root()); } }
            """
        );

        var views = Views()
            .In(solution)
            .Where(view => view.Name == "Value")
            .Select(view => view.Nullability)
            .Order();

        Assert.Equal([PropertyNullability.NonNullable, PropertyNullability.Nullable], views);
    }

    [Fact]
    public void Nullability_keeps_oblivious_and_open_declarations_distinct()
    {
        var solution = Analyze(
            """
            #nullable disable
            class Old { public string Text { get; init; } }
            #nullable enable
            class Open<T, TClass, TNotNull, TStruct, TMaybe>
                where TClass : class
                where TNotNull : notnull
                where TStruct : struct
                where TMaybe : class?
            {
                public T Plain { get; init; } = default!;
                public T? Annotated { get; init; }
                public TClass Reference { get; init; } = default!;
                public TNotNull NotNull { get; init; } = default!;
                public TStruct Value { get; init; }
                public TStruct? NullableValue { get; init; }
                public TMaybe Maybe { get; init; } = default!;
            }
            """
        );

        var views = Code.Types.PropertyViews().In(solution);

        Assert.Equal(
            [
                ("Text", PropertyNullability.Oblivious),
                ("Plain", PropertyNullability.TypeParameter),
                ("Annotated", PropertyNullability.Nullable),
                ("Reference", PropertyNullability.NonNullable),
                ("NotNull", PropertyNullability.NonNullable),
                ("Value", PropertyNullability.NonNullable),
                ("NullableValue", PropertyNullability.Nullable),
                ("Maybe", PropertyNullability.TypeParameter),
            ],
            views.Select(view => (view.Name, view.Nullability))
        );
        Assert.All(views, view => Assert.False(view.IsConstructed));
        Assert.All(views, view => Assert.Empty(view.ReachedFrom));
    }

    [Fact]
    public void Selection_defaults_match_type_traversal_and_each_option_widens_it()
    {
        var solution = Analyze(
            """
            #nullable enable
            class Base { public string Inherited { get; init; } = ""; }
            class Input : Base {
                public string Read { get; init; } = "";
                public string Write { set { } }
                internal string Internal { get; init; } = "";
                public string PrivateGet { private get; init; } = "";
                public static string Static { get; } = "";
                public string this[int index] => "";
            }
            """
        );
        var input = Code.Types.Named("Input");

        var byDefault = Names(input.PropertyViews(), solution);
        var withoutInherited = Names(input.PropertyViews(new() { Inherited = false }), solution);
        var withStatic = Names(input.PropertyViews(new() { IncludeStatic = true }), solution);
        var withIndexers = Names(input.PropertyViews(new() { IncludeIndexers = true }), solution);
        var withNonPublic = Names(input.PropertyViews(new() { IncludeNonPublic = true }), solution);
        var filtered = Names(
            input.PropertyViews(new() { Where = property => property.Name == "Read" }),
            solution
        );

        Assert.Equal(["Inherited", "Read"], byDefault);
        Assert.Equal(["Read"], withoutInherited);
        Assert.Equal(["Inherited", "Read", "Static"], withStatic);
        Assert.Equal(["Inherited", "Read", "this[]"], withIndexers);
        Assert.Equal(["Inherited", "Internal", "PrivateGet", "Read", "Write"], withNonPublic);
        Assert.Equal(["Read"], filtered);
    }

    [Fact]
    public void Accessors_are_exposed_including_for_synthesized_properties()
    {
        var solution = Analyze(
            """
            record class Input(string Name);
            record struct Mutable(string Name);
            class Written { public string Name { get; set; } = ""; }
            """
        );

        var views = Code.Types.PropertyViews().In(solution);

        Assert.Equal(
            [
                ("Input", true, false, true),
                ("Mutable", true, true, false),
                ("Written", true, true, false),
            ],
            views.Select(view =>
                (view.DeclaringType.Name, view.HasGetter, view.HasSetter, view.HasInit)
            )
        );
    }

    [Fact]
    public void Attributes_come_from_the_property_not_the_parameter()
    {
        var solution = Analyze(
            """
            using System;
            class RequiredAttribute : Attribute {}
            class ParameterOnlyAttribute : Attribute {}
            record Input([property: Required] string First, [ParameterOnly] string Second);
            """
        );

        var views = Code.Types.Named("Input").PropertyViews().In(solution);

        Assert.Equal(
            [("First", true), ("Second", false)],
            views.Select(view => (view.Name, view.HasAttribute(_required)))
        );
        Assert.False(views[1].HasAttribute(CodeType.Named("ParameterOnlyAttribute")));
    }

    [Fact]
    public void Generated_and_metadata_declarations_are_explicit_boundaries()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "Model.cs",
                    "class Model : System.Exception { public string Written { get; init; } = \"\"; } class H { void Ok(object value) {} void M() { Ok(new Model()); Ok(new Generated()); } }"
                ),
                new(
                    "Generated.g.cs",
                    "class Generated { public string Made { get; init; } = \"\"; }",
                    true
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var ordinary = Names(Views(), solution);
        var withGenerated = Names(Views(new() { IncludeGenerated = true }), solution);

        Assert.Equal(["Written"], ordinary);
        Assert.Equal(["Made", "Written"], withGenerated);
    }

    [Fact]
    public void Project_ownership_selects_where_properties_may_be_reported()
    {
        var workspace = fixture.Workspace();
        var library = workspace.AddProject(
            "Library",
            [new("Library.cs", "public class Base { public string Shared { get; init; } = \"\"; }")]
        );
        workspace.AddProject(
            "App",
            [
                new(
                    "App.cs",
                    "class Model : Base { public string Own { get; init; } = \"\"; } class H { void Ok(object value) {} void M() { Ok(new Model()); } }"
                ),
            ],
            dependencies: [library]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var all = Views().In(solution);
        var appOnly = Names(Views(new() { Owner = project => project.Name == "App" }), solution);

        Assert.Equal(
            [("Own", "App"), ("Shared", "Library")],
            all.Select(view => (view.Name, view.Source.Project.Name))
                .OrderBy(pair => pair.Item1, StringComparer.Ordinal)
        );
        Assert.Equal(["Own"], appOnly);
        Assert.All(
            all.Single(view => view.Name == "Shared").ReachedFrom,
            reached => Assert.Equal("App", reached.Seed.Source.Project.Name)
        );
    }

    [Fact]
    public void Incomplete_traversal_evidence_stays_on_the_view()
    {
        var solution = Analyze(
            """
            #nullable enable
            class Deep { public string Text { get; init; } = ""; }
            class Root { public string Name { get; init; } = ""; public Deep Next { get; init; } = new(); }
            class H { void Ok(object value) {} void M() { Ok(new Root()); } }
            """
        );
        var results = Code
            .Methods.TypeSeeds(
                TypeSeedSelector.InvocationArgument(CodeType.Named("H").Member("Ok"), "value")
            )
            .TraverseTypes(new TypeTraversal(maxDepth: 1).ThroughProperties());

        var views = results.PropertyViews().In(solution);

        Assert.Equal(["Text", "Name", "Next"], views.Select(view => view.Name));
        Assert.All(
            views,
            view => Assert.Equal(TypeTraversalStatus.DepthLimit, view.ReachedFrom.Single().Status)
        );
    }

    [Fact]
    public void Explicit_modifiers_read_the_property_not_a_positional_parameter()
    {
        var solution = Analyze(
            """
            class Written { public virtual string Name { get; set; } = ""; }
            record Input(params string[] Names);
            """
        );

        var views = Code.Types.PropertyViews().In(solution);

        Assert.Equal(
            [("Name", true, false), ("Names", false, false)],
            views.Select(view =>
                (
                    view.Name,
                    view.HasExplicitModifier(Modifier.Virtual),
                    view.HasExplicitModifier(Modifier.Params)
                )
            )
        );
    }

    private static CodeQuery<CodePropertyView> Views(PropertyViewOptions? options = null) =>
        Code
            .Methods.TypeSeeds(
                TypeSeedSelector.InvocationArgument(CodeType.Named("H").Member("Ok"), "value")
            )
            .TraverseTypes(new TypeTraversal().ThroughProperties())
            .PropertyViews(options);

    private static IEnumerable<string> Names(
        CodeQuery<CodePropertyView> views,
        AnalysisSolution solution
    ) => views.In(solution).Select(view => view.Name).Order(StringComparer.Ordinal);

    private static string Describe(CodePropertyView view) =>
        string.Join(
            " ",
            new[]
            {
                $"{view.DeclaringType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}.{view.Name}",
                view.Type.ToDisplayString(_format),
                view.Origin.ToString(),
                $"@{view.Location.Line}",
                view.Nullability.ToString(),
                view.HasAttribute(_required) ? "required" : null,
                view.Default is { } value ? $"{value.Kind}:{value.Value.Syntax}" : null,
            }.OfType<string>()
        );

    private static readonly SymbolDisplayFormat _format =
        SymbolDisplayFormat.CSharpErrorMessageFormat;

    private AnalysisSolution Analyze(string text)
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Inputs", [new("Inputs.cs", text)]);
        return workspace.Analyze(TestContext.Current.CancellationToken);
    }
}
