using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class NullableOriginTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    private const string Client = """
        using System.Threading.Tasks;
        public class Response { }
        public class Client
        {
            public Response Value = new();
            public Response Get() => new();
            public Task<Response> GetAsync() => Task.FromResult(new Response());
            public Response[] All = new Response[0];
            public static Client Shared = new();
        }
        """;

    [Fact]
    public void Values_from_oblivious_declarations_are_oblivious_while_annotated_ones_are_aware()
    {
        var source = $$"""
            #if OBLIVIOUS
            #nullable disable
            #endif
            {{Client}}
            #nullable enable
            class Example
            {
                bool A(Client c) => c.Get() != null;
                bool B(Client c) => c.Value is null;
                async Task<bool> C(Client c) { var r = await c.GetAsync(); var x = r; return x != null; }
                bool D(Client c) => c.All[0] != null;
            }
            """;
        var workspace = fixture.Workspace();
        workspace.AddProject("Annotated", [new("C.cs", source)]);
        workspace.AddProject("Oblivious", [new("C.cs", source)], symbols: ["OBLIVIOUS"]);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var origins = Code
            .NullChecks.In(solution)
            .Select(check =>
                $"{check.Source.Project.Name} {check.Condition.Syntax}: {check.CheckedValueOrigin}"
            )
            .ToArray();

        Assert.Equal(
            [
                "Annotated c.Get() != null: Aware",
                "Annotated c.Value is null: Aware",
                "Annotated x != null: Aware",
                "Annotated c.All[0] != null: Aware",
                "Oblivious c.Get() != null: Oblivious",
                "Oblivious c.Value is null: Oblivious",
                "Oblivious x != null: Oblivious",
                "Oblivious c.All[0] != null: Oblivious",
            ],
            origins
        );
    }

    [Fact]
    public void A_referenced_library_without_annotations_is_oblivious_though_flow_state_is_not_null()
    {
        var workspace = fixture.Workspace();
        var library = workspace.AddProject(
            "Library",
            [new("Client.cs", Client)],
            nullable: NullableContextOptions.Disable
        );
        workspace.AddProject(
            "App",
            [
                new(
                    "App.cs",
                    """
                    using System.Threading.Tasks;
                    class App
                    {
                        async Task<bool> Exists(Client client)
                        {
                            var key = await client.GetAsync();
                            return key != null;
                        }
                    }
                    """
                ),
            ],
            dependencies: [library]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var check = Code.NullChecks.In(solution).Single();

        Assert.True(check.IsKnownNotNullBeforeCheck);
        Assert.Equal(NullableOrigin.Oblivious, check.CheckedValueOrigin);
        Assert.Equal(
            ["Oblivious", "await client.GetAsync(): Oblivious"],
            Describe(check.CheckedValue.NullableOrigins())
        );
    }

    [Fact]
    public void Locals_follow_the_assignments_that_reach_the_read()
    {
        var analyses = Origins(
            """
            void Replaced(Client c) { var x = c.Get(); x = new Response(); _ = x != null; }
            void Merged(Client c, bool flag) { var x = new Response(); if (flag) x = c.Get(); _ = x != null; }
            void Before(Client c) { var x = new Response(); _ = x != null; x = c.Get(); }
            void Looped(Client c) { var x = new Response(); for (var i = 0; i < 2; i++) { _ = x != null; x = c.Get(); } }
            void Declared(Client c) { Response x; x = c.Get(); _ = x != null; }
            """
        );

        Assert.Equal(
            [
                ["Aware", "new Response(): Aware"],
                ["Oblivious", "new Response(): Aware", "c.Get(): Oblivious"],
                ["Aware", "new Response(): Aware"],
                ["Oblivious", "new Response(): Aware", "c.Get(): Oblivious"],
                ["Oblivious", "c.Get(): Oblivious"],
            ],
            analyses
        );
    }

    [Fact]
    public void Conditional_switch_coalesce_and_suppression_supply_their_alternatives()
    {
        var analyses = Origins(
            """
            void Conditional(Client c, bool flag) { var x = flag ? c.Get() : new Response(); _ = x != null; }
            void Selection(Client c, int n) { var x = n switch { 0 => new Response(), _ => c.Value! }; _ = x != null; }
            void Coalesce(Client c, Response? fallback) { var x = fallback ?? c.Get(); _ = x != null; }
            void Guarded(Client c) { var x = c.Get() ?? new Response(); _ = x != null; }
            void Assigned(Client c) { Response? x; _ = (x = c.Get()) != null; }
            void Cast(Client c, object o) { var x = o as Response; _ = x != null; }
            """
        );

        Assert.Equal(
            [
                ["Oblivious", "c.Get(): Oblivious", "new Response(): Aware"],
                ["Oblivious", "new Response(): Aware", "c.Value: Oblivious"],
                ["Oblivious", "fallback: Aware", "c.Get(): Oblivious"],
                ["Aware", "c.Get(): Aware", "new Response(): Aware"],
                ["Oblivious", "c.Get(): Oblivious"],
                ["Aware", "o: Aware"],
            ],
            analyses
        );
    }

    [Fact]
    public void Parameters_and_pattern_variables_supply_their_declared_value_and_later_assignments()
    {
        var analyses = Origins(
            """
            void Plain(Response? value) { _ = value != null; }
            void Reassigned(Client c, Response value) { value = c.Get(); _ = value != null; }
            void Optional(Client c, Response value, bool flag) { if (flag) value = c.Get(); _ = value != null; }
            void Lambda(Client c) { System.Func<Response, bool> test = r => r != null; }
            void Inferred(Client c) { System.Array.Find(c.All, r => r != null); }
            void Matched(object o) { if (o is Response x) _ = x != null; }
            """
        );

        Assert.Equal(
            [
                ["Aware", "value: Aware"],
                ["Oblivious", "c.Get(): Oblivious"],
                ["Oblivious", "value: Aware", "c.Get(): Oblivious"],
                ["Aware", "r: Aware"],
                ["Oblivious", "r: Oblivious"],
                ["Aware", "x: Aware"],
            ],
            analyses
        );
    }

    [Fact]
    public void Writes_that_flow_analysis_cannot_order_are_unknown()
    {
        var analyses = Origins(
            """
            void Out(Client c) { var x = new Response(); Fill(out x); _ = x != null; }
            void Captured(Client c) { var x = new Response(); System.Action a = () => x = new Response(); a(); _ = x != null; }
            void Late(Client c) { var x = new Response(); System.Func<bool> a = () => x != null; x = new Response(); }
            void Iterated(Client c) { foreach (var x in c.All) _ = x != null; }
            static void Fill(out Response r) => r = new();
            """
        );

        Assert.Equal(
            [
                ["Unknown", "x: UntrackedAssignment"],
                ["Unknown", "x: UntrackedAssignment"],
                ["Unknown", "x: UntrackedAssignment"],
                ["Unknown", "x: UntrackedAssignment"],
            ],
            analyses
        );
    }

    [Fact]
    public void An_oblivious_source_outweighs_an_unknown_one()
    {
        var analyses = Origins(
            """
            void Mixed(Client c, bool flag, dynamic d) { var x = flag ? c.Get() : (Response)d; _ = x != null; }
            """
        );

        Assert.Equal("Oblivious", analyses.Single()[0]);
    }

    [Fact]
    public void Assignments_in_a_try_reach_its_handlers()
    {
        var analyses = Origins(
            """
            void Caught(Client c)
            {
                var x = new Response();
                try { x = c.Get(); Throw(); x = new Response(); }
                catch (System.Exception) { _ = x != null; }
            }
            void Finally(Client c)
            {
                var x = new Response();
                try { x = c.Get(); } finally { }
                _ = x != null;
            }
            void InFinally(Client c)
            {
                var x = new Response();
                try { Throw(); } finally { x = c.Get(); }
                _ = x != null;
            }
            static void Throw() { }
            """
        );

        Assert.Equal(
            [
                [
                    "Oblivious",
                    "new Response(): Aware",
                    "c.Get(): Oblivious",
                    "new Response(): Aware",
                ],
                ["Oblivious", "c.Get(): Oblivious"],
                ["Oblivious", "new Response(): Aware", "c.Get(): Oblivious"],
            ],
            analyses
        );
    }

    [Fact]
    public void Nested_functions_follow_their_own_assignments()
    {
        var analyses = Origins(
            """
            void Local(Client c) { bool Test() { var x = new Response(); x = c.Get(); return x != null; } }
            void Lambda(Client c) { System.Func<bool> test = () => { var x = new Response(); x = c.Get(); return x != null; }; }
            """
        );

        Assert.Equal(
            [
                ["Oblivious", "c.Get(): Oblivious"],
                ["Oblivious", "c.Get(): Oblivious"],
            ],
            analyses
        );
    }

    [Fact]
    public void Members_of_generic_types_declared_without_annotations_stay_oblivious_when_constructed()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Inputs",
            [
                new(
                    "Inputs.cs",
                    """
                    using System.Threading.Tasks;
                    #nullable disable
                    public class Legacy<T>
                    {
                        public T Value;
                        public T Get() => default;
                        public T Item { get; set; }
                        public Task<T> GetAsync() => Task.FromResult(default(T));
                    }
                    #nullable enable
                    class Example
                    {
                        bool A(Legacy<string> legacy) { return legacy.Get() != null; }
                        bool B(Legacy<string> legacy) { return legacy.Value != null; }
                        bool C(Legacy<string> legacy) { return legacy.Item != null; }
                        async Task<bool> D(Legacy<string> legacy) { var x = await legacy.GetAsync(); return x != null; }
                        bool E(System.Collections.Generic.List<string> list) { return list[0] != null; }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var origins = Code
            .NullChecks.In(solution)
            .Select(check => $"{check.Condition.Syntax}: {check.CheckedValueOrigin}")
            .ToArray();

        Assert.Equal(
            [
                "legacy.Get() != null: Oblivious",
                "legacy.Value != null: Oblivious",
                "legacy.Item != null: Oblivious",
                "x != null: Oblivious",
                "list[0] != null: Aware",
            ],
            origins
        );
    }

    [Fact]
    public void Primary_constructor_parameters_of_types_split_across_files_are_unknown()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Inputs",
            [
                new(
                    "A.cs",
                    """
                    partial class Split(string text) { bool Has() => text != null; }
                    class Whole(string text) { bool Has() => text != null; }
                    """
                ),
                new("B.cs", "partial class Split { void Reset() { text = \"\"; } }"),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var origins = Code
            .NullChecks.In(solution)
            .Select(check => Describe(check.CheckedValue.NullableOrigins()).ToArray())
            .ToArray();

        Assert.Equal(
            [
                ["Unknown", "text: UntrackedAssignment"],
                ["Aware", "text: Aware"],
            ],
            origins
        );
    }

    [Fact]
    public void A_reassigned_setter_value_follows_its_assignment()
    {
        var analyses = Origins(
            """
            Response Assigned { set { value = Client.Shared.Get(); _ = value != null; } }
            Response Plain { set { _ = value != null; } }
            """
        );

        Assert.Equal(
            [
                ["Oblivious", "Client.Shared.Get(): Oblivious"],
                ["Aware", "value: Aware"],
            ],
            analyses
        );
    }

    [Fact]
    public void Invalid_bounds_are_rejected()
    {
        var check = Code.NullChecks.In(Analyze("void M(Response r) { _ = r != null; }")).Single();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            check.CheckedValue.NullableOrigins(maxDepth: -1)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            check.CheckedValue.NullableOrigins(maxResults: 0)
        );
    }

    [Fact]
    public void Local_hops_are_bounded_by_depth()
    {
        var check = Code
            .NullChecks.In(
                Analyze(
                    "void M(Client c) { var a = c.Get(); var b = a; var x = b; _ = x != null; }"
                )
            )
            .Single();

        var analysis = check.CheckedValue.NullableOrigins(maxDepth: 1);

        Assert.Equal(["Unknown", "b: DepthLimit"], Describe(analysis));
    }

    private string[][] Origins(string members) =>
        [
            .. Code
                .NullChecks.In(Analyze(members))
                .Select(check => Describe(check.CheckedValue.NullableOrigins()).ToArray()),
        ];

    private AnalysisSolution Analyze(string members)
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Inputs",
            [
                new(
                    "Inputs.cs",
                    $$"""
                    #nullable disable
                    {{Client}}
                    #nullable enable
                    class Example
                    {
                    {{members}}
                    }
                    """
                ),
            ],
            allowErrors: true
        );
        return workspace.Analyze(TestContext.Current.CancellationToken);
    }

    private static IEnumerable<string> Describe(NullableOriginAnalysis analysis) =>
        [
            analysis.Origin.ToString(),
            .. analysis.Values.Select(value => $"{value.Expression.Syntax}: {value.Origin}"),
            .. analysis.Boundaries.Select(stop => $"{stop.Expression.Syntax}: {stop.Reason}"),
        ];
}
