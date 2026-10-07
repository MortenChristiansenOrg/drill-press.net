using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class TypeQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void One_seed_reaches_multiple_substitutions_and_generated_intermediate_nodes()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "C.cs",
                    "class A {} class B {} class Box<T> { public T Value => default!; } class Root { public Box<A> First => new(); public Box<B> Second => new(); public Generated Third => new(); } class H { void Ok(object value) {} void M() { Ok(new Root()); } }"
                ),
                new("Generated.g.cs", "class Generated { public C Value => new(); }", true),
                new("Model.cs", "class C {}"),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var results = Code
            .Methods.TypeSeeds(
                TypeSeedSelector.InvocationArgument(CodeType.Named("H").Member("Ok"), "value")
            )
            .TraverseTypes(new TypeTraversal().ThroughProperties());

        var declarations = results.Definitions().In(solution);

        Assert.Equal(
            ["A", "B", "Box", "Root", "C"],
            declarations.Select(declaration => declaration.Name)
        );
        Assert.True(Assert.Single(results.In(solution)).IsComplete);
    }

    [Fact]
    public void An_absent_selected_attribute_type_remains_an_unresolved_seed()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "C.cs",
                    "class MarkerAttribute : System.Attribute { public System.Type? Payload { get; set; } } class H { [Marker] void M() {} }"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var results = Code
            .Methods.TypeSeeds(
                TypeSeedSelector.AttributeNamedType(CodeType.Named("MarkerAttribute"), "Payload")
            )
            .TraverseTypes(new TypeTraversal());

        var result = Assert.Single(results.In(solution));

        Assert.Equal(TypeTraversalStatus.Unresolved, result.Status);
        Assert.Empty(result.Types);
        Assert.Empty(results.Definitions().In(solution));
    }

    [Fact]
    public void Configured_attribute_call_and_constructor_seeds_use_original_static_types()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "C.cs",
                    """
                    class GenericAttribute<T>(int status) : System.Attribute { }
                    class PayloadAttribute(System.Type type, int status) : System.Attribute { }
                    class NamedAttribute : System.Attribute { public System.Type? Payload { get; set; } }
                    class A { } class B { } class C { } class D { } class E { }
                    class Result(object value) { }
                    class Handler {
                        static object Ok(object value) => value;
                        [Generic<A>(200), Payload(typeof(B), 200), Named(Payload = typeof(C))]
                        void Handle(object opaque) { _ = Ok(new D()); _ = new Result(new E()); _ = Ok(opaque); }
                        [Payload(typeof(string), 500)] void Error() { }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var seeds = Code.Methods.TypeSeeds(
            TypeSeedSelector.AttributeTypeArgument(CodeType.Named("GenericAttribute<>"), 0),
            TypeSeedSelector.AttributeConstructorType(
                CodeType.Named("PayloadAttribute"),
                "type",
                data => data.ConstructorArgument("status").Value.Value is 200
            ),
            TypeSeedSelector.AttributeNamedType(CodeType.Named("NamedAttribute"), "Payload"),
            TypeSeedSelector.InvocationArgument(CodeType.Named("Handler").Member("Ok"), "value"),
            TypeSeedSelector.ConstructorArgument(CodeType.Named("Result").Member(".ctor"), "value")
        );

        var found = seeds.In(solution);
        var results = seeds.TraverseTypes(new TypeTraversal()).In(solution);

        Assert.Equal(["A", "B", "C", "D", "Object", "E"], found.Select(seed => seed.Type!.Name));
        Assert.Equal(
            Enumerable.Repeat(TypeTraversalStatus.Complete, 6),
            results.Select(result => result.Status)
        );
        Assert.Equal(
            [
                "Generic<A>(200)",
                "Payload(typeof(B), 200)",
                "Named(Payload = typeof(C))",
                "new D()",
                "opaque",
                "new E()",
            ],
            found.Select(seed =>
                seed.Source.Tree.GetText().ToString(new(seed.Location.Start, seed.Location.Length))
            )
        );
    }

    [Fact]
    public void Constructed_generic_nodes_are_visited_before_partial_definition_deduplication()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "C.cs",
                    """
                    class A { public B? Child { get; set; } }
                    partial class B { public A? Parent { get; set; } }
                    partial class B { }
                    class Box<T> { public T Value => default!; }
                    class Page<T> { public T[] Items => []; }
                    class Handler { void Ok(object value) {} void M() { Ok(new Page<Box<A>>()); Ok(new Box<B>()); } }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var results = Code
            .Methods.TypeSeeds(
                TypeSeedSelector.InvocationArgument(CodeType.Named("Handler").Member("Ok"), "value")
            )
            .TraverseTypes(
                new TypeTraversal()
                    .Unwrap(CodeType.Named("Page<>"), [0])
                    .UnwrapArrays()
                    .ThroughProperties()
            )
            .WhereType(type => type.ContainingAssembly.Name == "Library");

        var declarations = results.Definitions().In(solution);
        var traversals = results.In(solution);

        Assert.Equal(["A", "B", "Box"], declarations.Select(declaration => declaration.Name));
        Assert.Equal([true, true], traversals.Select(result => result.IsComplete));
        Assert.Equal(
            ["Box<A>", "Box<B>"],
            traversals
                .SelectMany(result => result.Types)
                .Where(node => node.Type.Name == "Box")
                .Select(node => node.Type.ToDisplayString())
        );
    }

    [Fact]
    public void Ownership_uses_source_graph_and_retains_alternate_framework_contexts()
    {
        var workspace = fixture.Workspace();
        var first = workspace.AddProject(
            "Models",
            [
                new(
                    "Model.cs",
                    "public class Model { public Nested Value => new(); } public class Nested {}"
                ),
            ],
            framework: "net9.0"
        );
        var second = workspace.AddProject(
            "Models",
            [
                new(
                    "Model.cs",
                    "public class Model { public Nested Value => new(); } public class Nested {}"
                ),
            ],
            framework: "net10.0"
        );
        workspace.AddProject(
            "Api9",
            [new("H.cs", "class H { void Ok(object value) {} void M() { Ok(new Model()); } }")],
            dependencies: [first]
        );
        workspace.AddProject(
            "Api10",
            [new("H.cs", "class H { void Ok(object value) {} void M() { Ok(new Model()); } }")],
            dependencies: [second]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var results = Code
            .Methods.TypeSeeds(
                TypeSeedSelector.InvocationArgument(CodeType.Named("H").Member("Ok"), "value")
            )
            .TraverseTypes(new TypeTraversal().ThroughProperties());

        var declarations = results.Definitions(project => project.Name == "Models").In(solution);

        Assert.Equal(
            ["net9.0:Model", "net9.0:Nested", "net10.0:Model", "net10.0:Nested"],
            declarations.Select(declaration =>
                declaration.Source.Project.TargetFramework + ":" + declaration.Name
            )
        );
    }

    [Fact]
    public void Expanding_generic_recursion_exposes_limits_and_explicit_pruning_is_complete()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "C.cs",
                    "class Node<T> { public Node<System.Collections.Generic.List<T>> Next => new(); } class H { void Ok(object value) {} void M() { Ok(new Node<int>()); } }"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var seeds = Code.Methods.TypeSeeds(
            TypeSeedSelector.InvocationArgument(CodeType.Named("H").Member("Ok"), "value")
        );

        var depth = seeds
            .TraverseTypes(new TypeTraversal(maxDepth: 2).ThroughProperties())
            .In(solution);
        var states = seeds
            .TraverseTypes(new TypeTraversal(maxStates: 2).ThroughProperties())
            .In(solution);
        var stopped = seeds
            .TraverseTypes(
                new TypeTraversal().ThroughProperties().StopAt(type => type.Name == "Node")
            )
            .In(solution);

        Assert.Equal(
            [(TypeTraversalStatus.DepthLimit, 3)],
            depth.Select(result => (result.Status, result.VisitedCount))
        );
        Assert.Equal(
            [(TypeTraversalStatus.StateLimit, 2)],
            states.Select(result => (result.Status, result.VisitedCount))
        );
        Assert.Equal(
            [(TypeTraversalStatus.Complete, 1)],
            stopped.Select(result => (result.Status, result.VisitedCount))
        );
    }

    [Fact]
    public void Unresolved_edges_remain_incomplete_even_when_no_declarations_are_selected()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "C.cs",
                    "class Model { public Missing Value => default!; } class H { void Ok(object value) {} void M() { Ok(new Model()); } }"
                ),
            ],
            allowErrors: true
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var results = Code
            .Methods.TypeSeeds(
                TypeSeedSelector.InvocationArgument(CodeType.Named("H").Member("Ok"), "value")
            )
            .TraverseTypes(new TypeTraversal().ThroughProperties())
            .WhereType(_ => false);

        var status = results.In(solution).Select(result => result.Status).ToArray();
        var declarations = results.Definitions().In(solution);

        Assert.Equal([TypeTraversalStatus.Unresolved], status);
        Assert.Empty(declarations);
    }
}
