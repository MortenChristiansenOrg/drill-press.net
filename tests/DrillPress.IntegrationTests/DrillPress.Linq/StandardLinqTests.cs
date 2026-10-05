using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Presets;
using Xunit;

namespace DrillPress.IntegrationTests.Linq;

public sealed class StandardLinqTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Categories_distinguish_deferred_materialized_scalar_adapter_and_factory_calls()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Categories",
            [
                new(
                    "Calls.cs",
                    """
                    using System.Linq;
                    class C {
                        void M(int[] items, IQueryable<int> query) {
                            _ = items.Where(x => x > 0);
                            _ = items.Select(x => x + 1);
                            _ = items.OrderBy(x => x);
                            _ = Enumerable.ToList(query);
                            _ = items.ToDictionary(x => x);
                            _ = items.Any();
                            _ = query.Count();
                            _ = query.Where(x => x > 0);
                            _ = items.AsEnumerable();
                            _ = items.AsQueryable();
                            _ = Enumerable.Empty<int>();
                        }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var operations = Code.Calls.In(solution).Select(StandardLinq.Inspect).ToArray();

        Assert.All(
            operations,
            operation => Assert.Equal(LinqClassificationStatus.Supported, operation.Status)
        );
        Assert.Equal(
            new LinqOperationCategory?[]
            {
                LinqOperationCategory.DeferredConstruction,
                LinqOperationCategory.DeferredConstruction,
                LinqOperationCategory.DeferredConstruction,
                LinqOperationCategory.Materializer,
                LinqOperationCategory.Materializer,
                LinqOperationCategory.Scalar,
                LinqOperationCategory.Scalar,
                LinqOperationCategory.DeferredConstruction,
                LinqOperationCategory.Adapter,
                LinqOperationCategory.Adapter,
                LinqOperationCategory.SequenceFactory,
            },
            operations.Select(operation => operation.Category)
        );
        Assert.Equal(
            new LinqSurface?[]
            {
                LinqSurface.Enumerable,
                LinqSurface.Enumerable,
                LinqSurface.Enumerable,
                LinqSurface.Enumerable,
                LinqSurface.Enumerable,
                LinqSurface.Enumerable,
                LinqSurface.Queryable,
                LinqSurface.Queryable,
                LinqSurface.Enumerable,
                LinqSurface.Queryable,
                LinqSurface.Enumerable,
            },
            operations.Select(operation => operation.Surface)
        );
        Assert.Equal(
            Enumerable.Repeat("source", 10),
            operations.Take(10).Select(operation => Assert.Single(operation.SequenceInputs).Role)
        );
        Assert.Empty(operations[^1].SequenceInputs);
    }

    [Fact]
    public void Both_sequence_roles_preserve_source_order_and_original_types_in_either_spelling()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Inputs",
            [
                new(
                    "Inputs.cs",
                    """
                    using System.Linq;
                    class C {
                        void M(int[] items, IQueryable<int> query) {
                            _ = items.SequenceEqual(query);
                            _ = Enumerable.SequenceEqual(items, query);
                            _ = Enumerable.SequenceEqual(second: query, first: items);
                            _ = query.SequenceEqual(items);
                            _ = items.Zip(query, items);
                        }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var operations = Code.Calls.In(solution).Select(StandardLinq.Inspect).ToArray();
        var inputs = operations.SelectMany(operation => operation.SequenceInputs).ToArray();

        Assert.All(
            operations,
            operation => Assert.Equal(LinqClassificationStatus.Supported, operation.Status)
        );
        Assert.Equal(
            [
                "first:items",
                "second:query",
                "first:items",
                "second:query",
                "second:query",
                "first:items",
                "source1:query",
                "source2:items",
                "first:items",
                "second:query",
                "third:items",
            ],
            inputs.Select(input => input.Role + ":" + input.Value.Syntax)
        );
        Assert.Equal(
            [
                "int[]",
                "System.Linq.IQueryable<int>",
                "int[]",
                "System.Linq.IQueryable<int>",
                "System.Linq.IQueryable<int>",
                "int[]",
                "System.Linq.IQueryable<int>",
                "int[]",
                "int[]",
                "System.Linq.IQueryable<int>",
                "int[]",
            ],
            inputs.Select(input => input.Value.Type!.ToDisplayString())
        );
    }

    [Theory]
    [InlineData("net8.0", LinqClassificationStatus.UnsupportedOperation)]
    [InlineData("net9.0", LinqClassificationStatus.Supported)]
    [InlineData("net10.0", LinqClassificationStatus.Supported)]
    [InlineData("net10.0-windows", LinqClassificationStatus.Supported)]
    [InlineData("net11.0", LinqClassificationStatus.UnsupportedFramework)]
    public void New_overloads_are_supported_only_in_explicit_catalogue_versions(
        string framework,
        LinqClassificationStatus expected
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Version",
            [
                new(
                    "Version.cs",
                    """
                    using System.Linq;
                    class C { void M(int[] items) { _ = items.Index(); _ = items.Count(); } }
                    """
                ),
            ],
            framework: framework
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var operations = Code.Calls.In(solution).Select(StandardLinq.Inspect).ToArray();

        Assert.Equal(expected, operations[0].Status);
        Assert.Equal(
            expected == LinqClassificationStatus.UnsupportedFramework
                ? expected
                : LinqClassificationStatus.Supported,
            operations[1].Status
        );
        Assert.Equal(
            expected == LinqClassificationStatus.Supported
                ? LinqOperationCategory.DeferredConstruction
                : (LinqOperationCategory?)null,
            operations[0].Category
        );
        Assert.Equal(
            expected == LinqClassificationStatus.Supported ? 1 : 0,
            operations[0].SequenceInputs.Count
        );
    }

    [Fact]
    public void Custom_and_source_lookalike_declarations_are_never_standard_operations()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Custom",
            [
                new(
                    "Custom.cs",
                    """
                    namespace System.Linq {
                        public static class Enumerable { public static int Count(int[] source) => source.Length; }
                    }
                    class C {
                        static int Any(int[] source) => source.Length;
                        void M(int[] items) { _ = System.Linq.Enumerable.Count(items); _ = Any(items); }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var operations = Code.Calls.In(solution).Select(StandardLinq.Inspect).ToArray();

        Assert.All(
            operations,
            operation =>
                Assert.Equal(
                    new(LinqClassificationStatus.NotStandardSymbol, null, null, []),
                    operation
                )
        );
    }

    [Fact]
    public void Invalid_calls_have_no_classification_or_inputs()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Invalid",
            [
                new(
                    "Invalid.cs",
                    """
                    using System.Linq;
                    class C { void M(int[] items) { _ = Enumerable.Count(
                    #error unavailable input
                    items); } }
                    """
                ),
            ],
            allowErrors: true
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var operation = StandardLinq.Inspect(OperationQueries.Invocations.In(solution).Single());

        Assert.Equal(LinqClassificationStatus.Unresolved, operation.Status);
        Assert.Null(operation.Category);
        Assert.Null(operation.Surface);
        Assert.Empty(operation.SequenceInputs);
    }
}
