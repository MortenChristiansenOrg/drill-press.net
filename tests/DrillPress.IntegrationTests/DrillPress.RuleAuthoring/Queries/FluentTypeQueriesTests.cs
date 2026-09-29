using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Queries;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class FluentTypeQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Generic_arguments_include_containing_constructed_types()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Models",
            [
                new(
                    "Models.cs",
                    "class Outer<T> { public class Inner<U> {} } class Sensitive {} class Safe {} class C { void Send(object value) {} void M() { Send(new Outer<Sensitive>.Inner<Safe>()); } }"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var names = Code
            .Methods.TypesPassedAs("value", [CodeType.Named("C").Member("Send")])
            .TraverseTypes(new TypeTraversal().ThroughGenericArguments())
            .In(solution)
            .Single()
            .Types.Select(type => type.Type.Name)
            .ToArray();

        Assert.Equal(["Inner", "Safe", "Sensitive"], names);
    }

    [Fact]
    public void Opaque_wrapper_suppresses_property_and_custom_edges_in_either_configuration_order()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Models",
            [
                new(
                    "Models.cs",
                    """
                    class Leak {} class Leaf {} class Item { public Leaf Child => new(); }
                    class Page<T> { public T Item => default!; public Leak Metadata => new(); }
                    class C { static void Send(object value) {} void M() { Send(new Page<Item>()); } }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var seeds = Code.Methods.TypesPassedAs("value", [CodeType.Named("C").Member("Send")]);
        var page = CodeType.Named("Page<>");
        var leak = solution.Projects.Single().Compilation.GetTypeByMetadataName("Leak");
        var first = new TypeTraversal()
            .SkippingTypes(page)
            .ThroughProperties()
            .ThroughGenericArguments()
            .Follow(type => page.Matches(type) ? [new TypeEdge(leak)] : []);
        var last = new TypeTraversal()
            .ThroughProperties()
            .ThroughGenericArguments()
            .Follow(type => page.Matches(type) ? [new TypeEdge(leak)] : [])
            .SkippingTypes(page);

        var firstNames = seeds
            .TraverseTypes(first)
            .DeclaredInProject("Models")
            .In(solution)
            .Select(type => type.Name)
            .ToArray();
        var lastNames = seeds
            .TraverseTypes(last)
            .DeclaredInProject("Models")
            .In(solution)
            .Select(type => type.Name)
            .ToArray();
        var limited = seeds
            .TraverseTypes(last.WithinLimits(maxDepth: 0, maxTypes: 10))
            .In(solution)
            .Select(result => result.Status)
            .ToArray();

        Assert.Equal(["Leaf", "Item"], firstNames);
        Assert.Equal(firstNames, lastNames);
        Assert.Equal([TypeTraversalStatus.DepthLimit], limited);
    }

    [Fact]
    public void Grouped_type_sources_share_one_nested_policy_and_attribute_predicate()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Models",
            [
                new(
                    "Models.cs",
                    """
                    using System;
                    class ProducesAttribute<T>(int status) : Attribute {}
                    class ProducesAttribute(Type type, int status) : Attribute {}
                    class A {} class B {} class Cc {} class D {} class E {}
                    class Result(object value) {}
                    class C {
                        static void Send(object value) {}
                        [Produces<A>(200), Produces(typeof(B), 200)]
                        void M() { Send(new Cc()); Action later = () => Send(new D()); void Local() { _ = new Result(new E()); } }
                        [Produces<A>(500)] void Error() {}
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var targets = new[]
        {
            CodeType.Named("C").Member("Send"),
            CodeType.Named("Result").Constructor(),
        };
        var methods = Code.Methods.Named("M", "Error");

        var all = methods
            .TypesPassedAs("value", targets, nested: NestedFunctions.Include)
            .Union(
                methods.TypesDeclaredBy(
                    CodeType.Named("ProducesAttribute<>"),
                    CodeType.Named("ProducesAttribute"),
                    where: attribute => attribute.ConstructorValue<int>("status").Value == 200
                )
            )
            .In(solution)
            .Select(seed => seed.Type?.Name)
            .ToArray();
        var direct = methods
            .TypesPassedAs("value", targets)
            .In(solution)
            .Select(seed => seed.Type?.Name)
            .ToArray();

        Assert.Equal(["Cc", "D", "E", "A", "B"], all);
        Assert.Equal(["Cc"], direct);
    }
}
