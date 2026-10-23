using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class ScopeQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Type_method_and_loop_scopes_stop_at_their_own_boundaries()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "App",
            [
                new(
                    "A.cs",
                    """
                    using System;
                    namespace App;
                    class A
                    {
                        Action field = () => Console.WriteLine(1);
                        void M()
                        {
                            while (true)
                            {
                                Console.WriteLine(2);
                                Action nested = () => Console.WriteLine(3);
                                foreach (var item in new[] { 4 }) Console.WriteLine(item);
                                break;
                            }
                        }
                        class Inner { int Value => Console.Read(); }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var outer = CodeType.Named("App.A");
        var inner = CodeType.Named("App.A+Inner");

        var scopes = Code
            .Calls.ToMethodsNamed("WriteLine", "Read")
            .In(solution)
            .Select(call =>
                $"{call.Expression!.Syntax}:{call.EnclosingLoop()?.Syntax.Kind()}:{call.IsInLoop()}:{call.ContainingMethod()?.Name}:{call.IsInType(outer)}:{call.IsInType(inner)}"
            )
            .ToArray();

        Assert.Equal(
            [
                "Console.WriteLine(1)::False::True:False",
                "Console.WriteLine(2):WhileStatement:True:M:True:False",
                "Console.WriteLine(3)::False:M:True:False",
                "Console.WriteLine(item):ForEachStatement:True:M:True:False",
                "Console.Read()::False:get_Value:True:True",
            ],
            scopes
        );
    }
}
