using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Presets;
using Xunit;

namespace DrillPress.IntegrationTests.Linq;

public sealed class StandardLinqTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Documented_classification_pattern_reports_scoped_standard_gaps_with_the_original_context()
    {
        var workspace = fixture.Workspace();
        const string source = """
            using System.Linq;
            namespace Product.Data { class DataAccess {} }
            class C {
                static int Count(int[] items) => 0;
                void M(int[] items) {
                    _ = items.Index();
                    _ = items.Count();
                    _ = items.Where(x => x > 0);
                    _ = Count(items);
                }
            }
            """;
        workspace.AddProject("Supported", [new("Supported.cs", source)]);
        workspace.AddProject("Older", [new("Older.cs", source)], framework: "net8.0");
        workspace.AddProject("Future", [new("Future.cs", source)], framework: "net11.0");
        workspace.AddProject(
            "Tests",
            [new("Tests.cs", source)],
            framework: "net11.0",
            isTest: true
        );
        workspace.AddProject(
            "Unrelated",
            [new("Unrelated.cs", source.Replace("class DataAccess", "class Other"))],
            framework: "net11.0"
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var calls = Code
            .Calls.InNonTestProjects()
            .InProjectsWithType(CodeType.Named("Product.Data.DataAccess"));
        var classified = calls
            .Select(call => (Call: call, Operation: StandardLinq.Inspect(call)))
            .At(item => item.Call);
        var supportedTerminals = classified.Where(item =>
            item.Value.Operation.Status == LinqClassificationStatus.Supported
            && item.Value.Operation.Category
                is LinqOperationCategory.Scalar
                    or LinqOperationCategory.Materializer
        );
        var rules = new RuleSet();
        rules
            .For(
                classified.Where(item =>
                    item.Value.Operation.Status == LinqClassificationStatus.UnsupportedFramework
                )
            )
            .Forbid("LINQ_FRAMEWORK", "Use a supported framework.");
        rules
            .For(
                classified.Where(item =>
                    item.Value.Operation.Status == LinqClassificationStatus.UnsupportedOperation
                )
            )
            .Forbid("LINQ_OPERATION", "Review this overload.");
        rules
            .For(supportedTerminals.Select(item => item.Value.Call))
            .Forbid("TERMINAL", "Selected terminal.");

        var diagnostics = rules.Evaluate(solution);

        Assert.Equal(
            """
            LINQ_FRAMEWORK|Use a supported framework.|Future/net11.0|Future.cs|items.Index()|0
            LINQ_FRAMEWORK|Use a supported framework.|Future/net11.0|Future.cs|items.Count()|0
            LINQ_FRAMEWORK|Use a supported framework.|Future/net11.0|Future.cs|items.Where(x => x > 0)|0
            LINQ_OPERATION|Review this overload.|Older/net8.0|Older.cs|items.Index()|0
            TERMINAL|Selected terminal.|Older/net8.0|Older.cs|items.Count()|0
            TERMINAL|Selected terminal.|Supported/net10.0|Supported.cs|items.Count()|0
            """.Replace("\r\n", "\n"),
            string.Join(
                "\n",
                diagnostics.Select(diagnostic =>
                    $"{diagnostic.Descriptor.Id}|{diagnostic.Descriptor.Message}|{diagnostic.Source!.Project.Name}/{diagnostic.Source.Project.TargetFramework}|{diagnostic.Location.FilePath}|"
                    + diagnostic.Source.Document.Text.Substring(
                        diagnostic.Location.Start,
                        diagnostic.Location.Length
                    )
                    + $"|{diagnostic.Coverage.Count}"
                )
            )
        );
    }

    [Theory]
    [InlineData("net8.0")]
    [InlineData("net9.0")]
    [InlineData("net10.0")]
    [InlineData("net10.0-windows")]
    public void Consumption_facts_distinguish_non_enumerating_count_from_potential_consumption(
        string framework
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Consumption",
            [
                new(
                    "Calls.cs",
                    """
                    using System.Linq;
                    class C {
                        void M(int[] items, IQueryable<int> query) {
                            _ = items.TryGetNonEnumeratedCount(out var first);
                            _ = Enumerable.TryGetNonEnumeratedCount(items, out var second);
                            _ = items.Count();
                            _ = Enumerable.Count(items, x => x > 0);
                            _ = items.Any();
                            _ = items.ToList();
                            _ = items.SequenceEqual(query);
                            _ = items.Where(x => x > 0);
                            _ = items.AsEnumerable();
                            _ = Enumerable.Empty<int>();
                            _ = query.Count();
                            _ = query.Where(x => x > 0);
                        }
                    }
                    """
                ),
            ],
            framework: framework
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var descriptions = Code
            .Calls.In(solution)
            .Select(StandardLinq.Inspect)
            .Select(operation =>
                $"{operation.Status}/{operation.Surface}/{operation.Category}/{operation.SequenceConsumption}:"
                + string.Join(
                    ",",
                    operation.SequenceInputs.Select(input => input.Role + ":" + input.Value.Syntax)
                )
            );

        Assert.Equal(
            """
            Supported/Enumerable/Scalar/NeverEnumerates:source:items
            Supported/Enumerable/Scalar/NeverEnumerates:source:items
            Supported/Enumerable/Scalar/MayEnumerate:source:items
            Supported/Enumerable/Scalar/MayEnumerate:source:items
            Supported/Enumerable/Scalar/MayEnumerate:source:items
            Supported/Enumerable/Materializer/MayEnumerate:source:items
            Supported/Enumerable/Scalar/MayEnumerate:first:items,second:query
            Supported/Enumerable/DeferredConstruction/Unknown:source:items
            Supported/Enumerable/Adapter/Unknown:source:items
            Supported/Enumerable/SequenceFactory/Unknown:
            Supported/Queryable/Scalar/Unknown:source:query
            Supported/Queryable/DeferredConstruction/Unknown:source:query
            """.Replace("\r\n", "\n"),
            string.Join("\n", descriptions)
        );
    }

    [Fact]
    public void Potential_consumption_selection_excludes_count_inspection_and_preserves_both_sequence_roles()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Queries",
            [
                new(
                    "Calls.cs",
                    """
                    using System.Linq;
                    class C {
                        void M(int[] items, IQueryable<int> query) {
                            _ = items.TryGetNonEnumeratedCount(out var count);
                            _ = items.Count();
                            _ = items.SequenceEqual(query);
                            _ = Enumerable.SequenceEqual(second: query, first: items);
                        }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var sources = Code
            .Calls.Select(call => (Call: call, Operation: StandardLinq.Inspect(call)))
            .Where(item =>
                item.Operation.Status == LinqClassificationStatus.Supported
                && item.Operation.SequenceConsumption == LinqSequenceConsumption.MayEnumerate
            )
            .SelectMany(item => item.Operation.SequenceInputs)
            .In(solution);

        Assert.Equal(
            ["source:items", "first:items", "second:query", "second:query", "first:items"],
            sources.Select(input => input.Role + ":" + input.Value.Syntax)
        );
    }

    [Fact]
    public void Array_specific_overloads_preserve_the_original_sequence_input_in_either_spelling()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Arrays",
            [
                new(
                    "Calls.cs",
                    """
                    using System.Linq;
                    class C { void M(int[] items) { _ = items.Reverse(); _ = Enumerable.Reverse(items); } }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var operations = Code.Calls.In(solution).Select(StandardLinq.Inspect).ToArray();

        Assert.Equal(
            [LinqClassificationStatus.Supported, LinqClassificationStatus.Supported],
            operations.Select(operation => operation.Status)
        );
        Assert.Equal(
            new LinqOperationCategory?[]
            {
                LinqOperationCategory.DeferredConstruction,
                LinqOperationCategory.DeferredConstruction,
            },
            operations.Select(operation => operation.Category)
        );
        Assert.Equal(
            ["source:items:int[]", "source:items:int[]"],
            operations.Select(operation =>
            {
                var input = Assert.Single(operation.SequenceInputs);
                return input.Role
                    + ":"
                    + input.Value.Syntax
                    + ":"
                    + input.Value.Type!.ToDisplayString();
            })
        );
    }

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
        Assert.Equal(LinqSequenceConsumption.Unknown, operations[0].SequenceConsumption);
        Assert.Equal(
            expected == LinqClassificationStatus.UnsupportedFramework
                ? LinqSequenceConsumption.Unknown
                : LinqSequenceConsumption.MayEnumerate,
            operations[1].SequenceConsumption
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
                        static bool TryGetNonEnumeratedCount(int[] source, out int count) { count = 0; return false; }
                        void M(int[] items) { _ = System.Linq.Enumerable.Count(items); _ = Any(items); _ = TryGetNonEnumeratedCount(items, out var count); }
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
        Assert.Equal(LinqSequenceConsumption.Unknown, operation.SequenceConsumption);
    }
}
