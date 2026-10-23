using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class TimeProviderQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Waits_and_timers_on_the_system_clock_are_told_apart_from_those_given_a_TimeProvider()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "A",
            [
                new(
                    "A.cs",
                    """
                    using System;
                    using System.Threading;
                    using System.Threading.Tasks;
                    class A {
                        async Task M(TimeProvider clock, Task work) {
                            await Task.Delay(1);
                            await Task.Delay(TimeSpan.FromSeconds(1), clock);
                            await work.WaitAsync(TimeSpan.FromSeconds(1));
                            Thread.Sleep(1);
                            using var plain = new CancellationTokenSource();
                            plain.CancelAfter(1);
                            using var timed = new CancellationTokenSource(TimeSpan.FromSeconds(1), clock);
                            timed.CancelAfter(1);
                            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                            using var injected = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
                            using var delayed = new CancellationTokenSource(1);
                        }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var waits = Code
            .Calls.Where(call => call.WaitsWithoutTimeProvider())
            .In(solution)
            .Select(call => call.Operation.Syntax.ToString())
            .ToArray();
        var timers = Code
            .ObjectCreations.Where(creation => creation.TimesWithoutTimeProvider())
            .In(solution)
            .Select(creation => creation.Syntax.ToString())
            .ToArray();

        Assert.Equal(
            [
                "Task.Delay(1)",
                "work.WaitAsync(TimeSpan.FromSeconds(1))",
                "Thread.Sleep(1)",
                "plain.CancelAfter(1)",
            ],
            waits
        );
        Assert.Equal(
            ["new PeriodicTimer(TimeSpan.FromSeconds(1))", "new CancellationTokenSource(1)"],
            timers
        );
    }
}
