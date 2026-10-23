using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Semantics;

public sealed class TaskTypesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Framework_tasks_are_recognized_and_unwrapped_to_their_result()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "A",
            [
                new(
                    "A.cs",
                    """
                    using System.Collections.Generic;
                    using System.Threading.Tasks;
                    class A {
                        Task Run() => Task.CompletedTask;
                        Task<string> Read() => Task.FromResult("");
                        ValueTask<int> Count() => new(1);
                        IAsyncEnumerable<int> Stream() => null!;
                        int Sync() => 1;
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var returns = Code
            .Methods.In(solution)
            .Select(method =>
                $"{method.Name}:{method.Symbol!.ReturnType.IsTask()}:{method.Symbol!.ReturnType.UnwrapTask()}"
            )
            .ToArray();

        Assert.Equal(
            [
                "Run:True:System.Threading.Tasks.Task",
                "Read:True:string",
                "Count:True:int",
                "Stream:False:System.Collections.Generic.IAsyncEnumerable<int>",
                "Sync:False:int",
            ],
            returns
        );
    }
}
