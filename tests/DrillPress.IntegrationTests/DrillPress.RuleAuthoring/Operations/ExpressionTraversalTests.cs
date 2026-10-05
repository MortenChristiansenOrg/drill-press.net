using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class ExpressionTraversalTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Configured_adapters_preserve_extension_spellings_named_inputs_and_original_types()
    {
        var solution = Analyze(
            """
            using System.Collections.Generic;
            using System.Linq;
            class Adapters {
                static IEnumerable<int> Wrap(string label, IEnumerable<int> source) => source;
                void M(IQueryable<int> query) {
                    _ = Wrap(source: (query.AsEnumerable()), label: "first");
                    _ = Wrap("second", Enumerable.AsEnumerable(query));
                }
            }
            """
        );
        var traversal = new ExpressionTraversal()
            .ThroughReceiverOf(CodeType.Framework("System.Linq.Enumerable").Member("AsEnumerable"))
            .ThroughArgumentOf(CodeType.Named("Adapters").Member("Wrap"), "source");
        var roots = Code.Calls.ToMethodsNamed("Wrap").Expressions().In(solution);

        var results = roots.Select(root => root.TraverseInputs(traversal)).ToArray();

        Assert.Equal(
            [ExpressionTraversalStatus.Complete, ExpressionTraversalStatus.Complete],
            results.Select(result => result.Status)
        );
        Assert.Equal(
            [
                "Wrap(source: (query.AsEnumerable()), label: \"first\")",
                "(query.AsEnumerable())",
                "query",
            ],
            results[0].Values.Select(value => value.Syntax.ToString())
        );
        Assert.Equal(
            [
                "Wrap(\"second\", Enumerable.AsEnumerable(query))",
                "Enumerable.AsEnumerable(query)",
                "query",
            ],
            results[1].Values.Select(value => value.Syntax.ToString())
        );
        Assert.All(
            results,
            result =>
            {
                Assert.Empty(result.Boundaries);
                Assert.Same(roots[0].Source, result.Values[^1].Source);
                Assert.True(result.Values[^1].TypeIs(CodeType.Of<IQueryable<int>>()));
                Assert.True(result.Values[^1].ConvertedTypeIs(CodeType.Of<IEnumerable<int>>()));
            }
        );
    }

    [Fact]
    public void Unconfigured_calls_assignments_and_properties_are_source_boundaries()
    {
        var solution = Analyze(
            """
            class Adapters {
                static object Wrap(object source) => source;
                object Property => new object();
                void M(object source) { var alias = Wrap(source); _ = Wrap(alias); _ = Wrap(Property); }
            }
            """
        );
        var calls = Code.Calls.ToMethodsNamed("Wrap").Expressions().In(solution);
        var empty = new ExpressionTraversal();
        var configured = empty.ThroughArgumentOf(
            CodeType.Named("Adapters").Member("Wrap"),
            "source"
        );

        var unconfigured = calls[0].TraverseInputs(empty);
        var results = calls.Skip(1).Select(call => call.TraverseInputs(configured)).ToArray();

        Assert.Equal(ExpressionTraversalStatus.Complete, unconfigured.Status);
        Assert.Equal(
            ["Wrap(source)"],
            unconfigured.Values.Select(value => value.Syntax.ToString())
        );
        Assert.Equal(
            ["Wrap(alias)", "alias"],
            results[0].Values.Select(value => value.Syntax.ToString())
        );
        Assert.Equal(
            ["Wrap(Property)", "Property"],
            results[1].Values.Select(value => value.Syntax.ToString())
        );
        Assert.All(
            results,
            result => Assert.Equal(ExpressionTraversalStatus.Complete, result.Status)
        );
    }

    [Fact]
    public void Repeated_configured_roles_do_not_duplicate_values_and_bounds_are_explicit()
    {
        var solution = Analyze(
            """
            class Adapters {
                static object Wrap(object source) => source;
                void M(object source) { _ = Wrap(Wrap(Wrap(source))); }
            }
            """
        );
        var root = Code.Calls.ToMethodsNamed("Wrap").Expressions().In(solution).First();
        var member = CodeType.Named("Adapters").Member("Wrap");
        var traversal = new ExpressionTraversal()
            .ThroughArgumentOf(member, "source")
            .ThroughArgumentOf(member, "source");

        var complete = root.TraverseInputs(traversal);
        var depth = root.TraverseInputs(traversal, maxDepth: 1);
        var count = root.TraverseInputs(traversal, maxExpressions: 2);

        Assert.Equal(
            ["Wrap(Wrap(Wrap(source)))", "Wrap(Wrap(source))", "Wrap(source)", "source"],
            complete.Values.Select(value => value.Syntax.ToString())
        );
        Assert.Equal(ExpressionTraversalStatus.Complete, complete.Status);
        Assert.Equal(ExpressionTraversalStatus.LimitExceeded, depth.Status);
        Assert.Equal(
            [ExpressionTraversalReason.DepthLimit],
            depth.Boundaries.Select(boundary => boundary.Reason)
        );
        Assert.Equal(
            ["Wrap(Wrap(Wrap(source)))", "Wrap(Wrap(source))"],
            depth.Values.Select(value => value.Syntax.ToString())
        );
        Assert.Equal(ExpressionTraversalStatus.LimitExceeded, count.Status);
        Assert.Equal(
            [ExpressionTraversalReason.ExpressionLimit],
            count.Boundaries.Select(boundary => boundary.Reason)
        );
        Assert.Equal(
            ["Wrap(Wrap(Wrap(source)))", "Wrap(Wrap(source))"],
            count.Values.Select(value => value.Syntax.ToString())
        );
    }

    [Fact]
    public void Missing_explicit_inputs_and_unsupported_conversions_are_unavailable()
    {
        var solution = Analyze(
            """
            class Adapters {
                static object Wrap(object? source = null) => source!;
                void M(string source) { _ = Wrap(); _ = Wrap((object)source); }
            }
            """
        );
        var roots = Code.Calls.ToMethodsNamed("Wrap").Expressions().In(solution);
        var traversal = new ExpressionTraversal().ThroughArgumentOf(
            CodeType.Named("Adapters").Member("Wrap"),
            "source"
        );

        var results = roots.Select(root => root.TraverseInputs(traversal)).ToArray();

        Assert.Equal(
            [ExpressionTraversalStatus.Unavailable, ExpressionTraversalStatus.Unavailable],
            results.Select(result => result.Status)
        );
        Assert.Equal(
            [ExpressionTraversalReason.UnavailableInput],
            results[0].Boundaries.Select(boundary => boundary.Reason)
        );
        Assert.Equal(
            [ExpressionTraversalReason.UnsupportedExpression],
            results[1].Boundaries.Select(boundary => boundary.Reason)
        );
        Assert.Equal(
            ["Wrap((object)source)", "(object)source"],
            results[1].Values.Select(value => value.Syntax.ToString())
        );
    }

    [Fact]
    public void Invalid_binding_returns_an_explicit_unavailable_result()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Adapters",
            [new("Adapters.cs", "class C { void M() { Missing(); } }")],
            allowErrors: true
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var node = Code.Nodes<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .In(solution)
            .Single();
        var expression = new CodeExpression(node.Source, node.Syntax);

        var result = expression.TraverseInputs(new ExpressionTraversal());

        Assert.Equal(ExpressionTraversalStatus.Unavailable, result.Status);
        Assert.Same(expression, Assert.Single(result.Values));
        Assert.Equal(
            [new ExpressionTraversalBoundary(expression, ExpressionTraversalReason.InvalidBinding)],
            result.Boundaries
        );
    }

    private AnalysisSolution Analyze(string text)
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Adapters", [new("Adapters.cs", text)]);
        return workspace.Analyze(TestContext.Current.CancellationToken);
    }
}
