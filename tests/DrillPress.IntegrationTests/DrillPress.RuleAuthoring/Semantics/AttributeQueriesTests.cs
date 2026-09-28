using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Semantics;

public sealed class AttributeQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Derived_markers_match_the_marker_assembly_and_partial_owners_are_distinct_per_context()
    {
        var workspace = fixture.Workspace();
        var markers = workspace.AddProject(
            "Markers",
            [new("Markers.cs", "public class FactAttribute : System.Attribute {}")]
        );
        workspace.AddProject(
            "Tests",
            [
                new(
                    "A.cs",
                    "class CustomAttribute : FactAttribute {} partial class Tests { [Custom] void First() {} }"
                ),
                new("B.cs", "partial class Tests { [Fact] void Second() {} }"),
            ],
            dependencies: [markers]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var methods = Code.Methods.WithAttribute(CodeType.Named("FactAttribute", "Markers"));

        var names = methods.In(solution).Select(method => method.Name).ToArray();
        var owners = methods.ContainingTypes().In(solution).Select(owner => owner.Name).ToArray();
        var wrongAssembly = Code
            .Methods.WithAttribute(CodeType.Named("FactAttribute", "Tests"))
            .In(solution);

        Assert.Equal(["First", "Second"], names);
        Assert.Equal(["Tests"], owners);
        Assert.Empty(wrongAssembly);
    }

    [Fact]
    public void Named_argument_absence_is_distinct_from_explicit_false_and_null()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Attributes",
            [
                new(
                    "A.cs",
                    """
                    class MarkerAttribute : System.Attribute { public bool Allow { get; set; } public string? Name { get; set; } }
                    class C {
                        [Marker] void Missing() {}
                        [Marker(Allow = false, Name = null)] void Explicit() {}
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var values = Code
            .Methods.WithAttribute(CodeType.Named("MarkerAttribute"))
            .In(solution)
            .Select(method => method.Symbol!.Attributes().Single())
            .Select(attribute =>
                $"{attribute.NamedArgument("Allow").HasValue}:{attribute.NamedArgument("Name").HasValue}"
            )
            .ToArray();

        Assert.Equal(["False:False", "True:True"], values);
    }

    [Fact]
    public void An_override_does_not_implicitly_inherit_the_base_methods_attributes()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Attributes",
            [
                new(
                    "A.cs",
                    """
                    class MarkerAttribute : System.Attribute {}
                    class Base { [Marker] public virtual void M() {} }
                    class Derived : Base { public override void M() {} }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var owners = Code
            .Methods.WithAttribute(CodeType.Named("MarkerAttribute"))
            .ContainingTypes()
            .In(solution)
            .Select(type => type.Name)
            .ToArray();

        Assert.Equal(["Base"], owners);
    }
}
