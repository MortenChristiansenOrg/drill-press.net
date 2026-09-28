using DrillPress.Collections;
using DrillPress.Configuration;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Operations;
using DrillPress.Queries;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Collections;

public sealed class ExpressionGroupsTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Constants_combine_selected_partial_occurrences_in_stable_order_and_keep_nested_types_separate()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new("B.cs", "partial class A { void N() { Use(\"x\"); Use(\"other\"); } }"),
                new(
                    "A.cs",
                    "partial class A { static void Use(string value) {} void M() { Use(\"x\"); Use(\"x\"); } class Nested { void M() { Use(\"x\"); Use(\"x\"); } } }"
                ),
            ]
        );
        var expressions = Selected();
        var repeatedInputs = CodeQuery<CodeExpression>.Create(solution =>
            expressions.In(solution).Concat(expressions.In(solution))
        );
        var query = ExpressionGroups.Constants(repeatedInputs);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var groups = query.In(solution);

        Assert.Equal(
            [("A", 3), ("Nested", 2)],
            groups.Select(group => (group.Owner.Name, group.Occurrences.Count))
        );
        Assert.Equal(
            ["A.cs", "A.cs", "B.cs"],
            groups[0].Occurrences.Select(occurrence => occurrence.Expression.Source.Document.Path)
        );
        Assert.Same(groups, query.In(solution));
    }

    [Fact]
    public void Typed_values_distinguish_null_text_numbers_and_enum_types()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "enum E { A } enum F { A } class A { static void Use(object? value) {} void M() { Use(null); Use(null); Use(\"null\"); Use(\"null\"); Use(1); Use(1L); Use(E.A); Use(F.A); } }"
                ),
            ]
        );

        var groups = ExpressionGroups
            .Constants(Selected())
            .In(workspace.Analyze(TestContext.Current.CancellationToken));

        Assert.Equal(
            [new string?[] { null, null }, new string?[] { "null", "null" }],
            groups.Select(group =>
                group
                    .Occurrences.Select(occurrence => (string?)occurrence.Expression.Constant.Value)
                    .ToArray()
            )
        );
    }

    [Theory]
    [InlineData("$\"/items/{a}\"", "$\"/items/{b}\"", 1)]
    [InlineData("\"/items/\" + a", "\"/items/\" + b", 1)]
    [InlineData("$\"/items/{a}\"", "$\"/other/{b}\"", 0)]
    [InlineData("$\"/items/{a}\"", "\"/items/\" + b", 0)]
    [InlineData("$\"{a}/{a}\"", "$\"{b}/{b}\"", 0)]
    [InlineData("$\"{a,3:X}\"", "$\"{b,4:X}\"", 0)]
    [InlineData("$\"{a:X}\"", "$\"{b:D}\"", 0)]
    public void Template_shapes_preserve_operators_formatting_and_exactly_one_hole(
        string first,
        string second,
        int count
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    $"class A {{ static void Use(string value) {{}} void M(int a, int b) {{ Use({first}); Use({second}); }} }}"
                ),
            ]
        );

        var groups = ExpressionGroups
            .OneHoleTemplates(
                Selected(),
                new(
                    TemplateShapes.Interpolation | TemplateShapes.Concatenation,
                    capture => capture.TypeIs(CodeType.Of<int>())
                )
            )
            .In(workspace.Analyze(TestContext.Current.CancellationToken));

        Assert.Equal(count, groups.Count);
        Assert.Equal(
            Enumerable.Repeat(new[] { "a", "b" }, count),
            groups.Select(group =>
                group
                    .Occurrences.Select(occurrence => occurrence.Capture!.Syntax.ToString())
                    .ToArray()
            )
        );
    }

    [Fact]
    public void Configured_calls_retain_bound_overloads_and_capture_restrictions()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { static void Use(string value) {} static string Encode(string value) => value; static string Encode(object value) => value.ToString()!; void M(string a, string b, object c) { Use($\"/{Encode(a)}\"); Use($\"/{Encode(b)}\"); Use($\"/{Encode(c)}\"); } }"
                ),
            ]
        );
        var query = ExpressionGroups.OneHoleTemplates(
            Selected(),
            new(
                TemplateShapes.Interpolation,
                _ => true,
                new ApiSet(CodeType.Named("A").Member("Encode"))
            )
        );

        var groups = query.In(workspace.Analyze(TestContext.Current.CancellationToken));

        Assert.Equal(
            [new[] { "a", "b" }],
            groups.Select(group =>
                group
                    .Occurrences.Select(occurrence => occurrence.Capture!.Syntax.ToString())
                    .ToArray()
            )
        );
    }

    [Fact]
    public void Grouping_never_combines_contexts_or_unresolved_expressions()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "First",
            [new("A.cs", "class A { static void Use(string value) {} void M() { Use(\"x\"); } }")]
        );
        workspace.AddProject(
            "Second",
            [
                new(
                    "B.cs",
                    "class A { static void Use(string value) {} void M() { Use(\"x\"); Use(missing); Use(missing); } }"
                ),
            ],
            allowErrors: true
        );

        var groups = ExpressionGroups
            .Constants(Selected())
            .In(workspace.Analyze(TestContext.Current.CancellationToken));

        Assert.Empty(groups);
    }

    [Theory]
    [InlineData("$\"{a}\"", "$\"{b}\"", 1)]
    [InlineData("$\"{P}\"", "$\"{P}\"", 0)]
    [InlineData("$\"{Encode(a)}\"", "$\"{Encode(b)}\"", 0)]
    public void Hidden_dependencies_and_unapproved_calls_are_not_template_captures(
        string first,
        string second,
        int expectedCount
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    $"class A {{ static string P => \"p\"; static string Encode(string value) => value; static void Use(string value) {{}} void M(string a, string b) {{ Use({first}); Use({second}); }} }}"
                ),
            ]
        );
        var query = ExpressionGroups.OneHoleTemplates(
            Selected(),
            new(TemplateShapes.Interpolation, _ => true)
        );

        var groups = query.In(workspace.Analyze(TestContext.Current.CancellationToken));

        Assert.Equal(expectedCount, groups.Count);
    }

    [Fact]
    public void Complexity_and_consumer_capture_boundaries_withhold_candidate_groups()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { static void Use(string value) {} void M(string a, string b) { Use($\"/{a}\"); Use($\"/{b}\"); } }"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var bounded = ExpressionGroups.OneHoleTemplates(
            Selected(),
            new(TemplateShapes.Interpolation, _ => true, maximumNodes: 1)
        );
        var disallowed = ExpressionGroups.OneHoleTemplates(
            Selected(),
            new(TemplateShapes.Interpolation, _ => false)
        );

        var boundedGroups = bounded.In(solution);
        var disallowedGroups = disallowed.In(solution);

        Assert.Empty(boundedGroups);
        Assert.Empty(disallowedGroups);
    }

    [Fact]
    public void Additional_equivalence_can_require_identical_constant_spelling()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { static void Use(int value) {} void M() { Use(1); Use(0x1); Use(1); } }"
                ),
            ]
        );
        var query = ExpressionGroups.Constants(
            Selected(),
            additionalEquivalence: (first, second) =>
                first.Syntax.ToString() == second.Syntax.ToString()
        );

        var groups = query.In(workspace.Analyze(TestContext.Current.CancellationToken));

        Assert.Equal(
            [new[] { "1", "1" }],
            groups.Select(group =>
                group
                    .Occurrences.Select(occurrence => occurrence.Expression.Syntax.ToString())
                    .ToArray()
            )
        );
    }

    [Fact]
    public void Template_capture_nullability_remains_part_of_equivalence()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { static void Use(string value) {} void M(string? a, string b) { Use($\"/{a}\"); Use($\"/{b}\"); } }"
                ),
            ]
        );
        var query = ExpressionGroups.OneHoleTemplates(
            Selected(),
            new(TemplateShapes.Interpolation, _ => true)
        );

        var groups = query.In(workspace.Analyze(TestContext.Current.CancellationToken));

        Assert.Empty(groups);
    }

    private static CodeQuery<CodeExpression> Selected() =>
        Sources
            .Nodes<ArgumentSyntax>()
            .Where(node =>
                node.Syntax.Parent?.Parent
                    is InvocationExpressionSyntax
                    {
                        Expression: IdentifierNameSyntax { Identifier.ValueText: "Use" }
                    }
            )
            .Select(node => new CodeExpression(node.Source, node.Syntax.Expression));
}
