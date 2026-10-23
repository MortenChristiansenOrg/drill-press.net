using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Semantics;

public sealed class ScopeValueQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Values_in_scope_are_usable_values_of_the_type_nearest_first()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Scopes",
            [
                new(
                    "Scopes.cs",
                    """
                    using System;
                    using System.Threading;
                    class FakeClock : TimeProvider { }
                    class Service(TimeProvider injected)
                    {
                        private readonly TimeProvider _clock = TimeProvider.System;
                        private static TimeProvider Shared = TimeProvider.System;
                        TimeProvider? Optional { get; }
                        void Run(CancellationToken cancellationToken, int count)
                        {
                            var later = new FakeClock();
                            Action<CancellationToken> work = inner => Use(inner);
                            var after = TimeProvider.System;
                        }
                        static void Static(CancellationToken token) => Use(token);
                        static void Use(object value) { }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var uses = Code.Calls.ToMethodsNamed("Use").In(solution);

        var tokens = uses[0].ValuesInScope<CancellationToken>();
        var clocks = uses[0].ValuesInScope(CodeType.Of<TimeProvider>());
        var staticClocks = uses[1].ValuesInScope<TimeProvider>();

        Assert.Equal(
            [
                ("inner", ScopedValueKind.Parameter),
                ("cancellationToken", ScopedValueKind.Parameter),
            ],
            tokens.Select(value => (value.Name, value.Kind))
        );
        Assert.Equal(
            [
                ("later", ScopedValueKind.Local),
                ("injected", ScopedValueKind.Parameter),
                ("Optional", ScopedValueKind.Property),
                ("Shared", ScopedValueKind.Field),
                ("_clock", ScopedValueKind.Field),
            ],
            clocks.Select(value => (value.Name, value.Kind))
        );
        Assert.Equal("FakeClock", clocks[0].Type.Name);
        Assert.Equal(["Shared"], staticClocks.Select(value => value.Name));
    }

    [Fact]
    public void Values_outside_a_static_function_or_in_an_outer_type_are_not_in_scope()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Scopes",
            [
                new(
                    "Scopes.cs",
                    """
                    using System;
                    using System.Threading;
                    class Outer(CancellationToken primary)
                    {
                        private readonly CancellationToken _outer = default;
                        private static CancellationToken Shared = default;
                        void Run(CancellationToken method)
                        {
                            var local = method;
                            Action<CancellationToken> work = static inner =>
                            {
                                var own = inner;
                                Use(1);
                            };
                            Count(method);
                            static void Count(CancellationToken counted) => Use(2);
                        }
                        class Inner
                        {
                            private readonly CancellationToken _inner = default;
                            void Run() => Use(3);
                        }
                        static void Use(object value) { }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var uses = Code.Calls.ToMethodsNamed("Use").In(solution);

        var names = uses.Select(use =>
                string.Join(",", use.ValuesInScope<CancellationToken>().Select(value => value.Name))
            )
            .ToArray();

        Assert.Equal(["own,inner,Shared", "counted,Shared", "Shared,_inner"], names);
    }
}
