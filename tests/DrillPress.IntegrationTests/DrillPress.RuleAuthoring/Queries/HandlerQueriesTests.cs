using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class HandlerQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Handlers_stop_at_nested_functions_and_catch_alls_guard_regardless_of_filters()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "A",
            [
                new(
                    "A.cs",
                    """
                    using System;
                    using System.Threading.Tasks;
                    class A {
                        async Task Run() {
                            try {} finally {
                                await Task.Delay(1);
                                try { await Task.Delay(2); } catch (Exception) {}
                                Func<Task> nested = async () => await Task.Delay(3);
                            }
                            try { await Task.Delay(4); } catch { await Task.Delay(5); }
                            try { try {} catch (InvalidOperationException) { await Task.Delay(6); } } catch (Exception) when (true) {}
                        }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var handlers = Code.Nodes<AwaitExpressionSyntax>()
            .In(solution)
            .Select(node => $"{node.Syntax}:{node.UnguardedHandler()}:{node.IsGuardedByCatchAll()}")
            .ToArray();

        Assert.Equal(
            [
                "await Task.Delay(1):Finally:False",
                "await Task.Delay(2):None:True",
                "await Task.Delay(3):None:False",
                "await Task.Delay(4):None:True",
                "await Task.Delay(5):Catch:False",
                "await Task.Delay(6):Catch:True",
            ],
            handlers
        );
    }
}
