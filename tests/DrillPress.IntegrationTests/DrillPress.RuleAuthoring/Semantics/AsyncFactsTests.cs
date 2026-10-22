using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Semantics;

public sealed class AsyncFactsTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Framework_task_shapes_and_signature_ownership_are_explicit()
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
                    using System.Collections.Generic;
                    interface I { Task Contract(); }
                    class Base { public virtual Task Override() => Task.CompletedTask; }
                    class A : Base, I {
                        public Task Contract() => Task.CompletedTask;
                        public override Task Override() => Task.CompletedTask;
                        public ValueTask<int> Value() => new(1);
                        public IAsyncEnumerable<int> Stream() => null!;
                        public void Event(object sender, EventArgs args) {}
                        public int Sync() => 1;
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var facts = Code
            .Methods.In(solution)
            .Where(method => method.Symbol!.ContainingType.Name == "A")
            .Select(method =>
                (
                    method.Name,
                    AsyncFacts.IsTask(method.Symbol!.ReturnType),
                    AsyncFacts.IsAsyncReturnType(method.Symbol.ReturnType),
                    AsyncFacts.IsDictatedBySignatureElsewhere(method.Symbol),
                    AsyncFacts.IsEventHandler(method.Symbol),
                    AsyncFacts.UnwrapTask(method.Symbol.ReturnType).ToDisplayString()
                )
            )
            .ToArray();

        Assert.Equal(
            new[]
            {
                ("Contract", true, true, true, false, "System.Threading.Tasks.Task"),
                ("Override", true, true, true, false, "System.Threading.Tasks.Task"),
                ("Value", true, true, false, false, "int"),
                (
                    "Stream",
                    false,
                    true,
                    false,
                    false,
                    "System.Collections.Generic.IAsyncEnumerable<int>"
                ),
                ("Event", false, false, false, true, "void"),
                ("Sync", false, false, false, false, "int"),
            },
            facts
        );
    }

    [Fact]
    public void Await_handlers_stop_at_nested_functions_and_recognize_catch_all_guards()
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
                                try { await Task.Delay(2).ConfigureAwait(false); } catch (Exception) {}
                                Func<Task> nested = async () => await Task.Delay(3);
                            }
                        }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var facts = Code.Nodes<AwaitExpressionSyntax>()
            .In(solution)
            .Select(node =>
                (
                    node.Syntax.ToString(),
                    AsyncFacts.UnguardedHandler(node.Syntax, node.Source.Model)?.GetType().Name,
                    AsyncFacts.IsGuardedInFunction(node.Syntax, node.Source.Model),
                    AsyncFacts.AwaitedCall(node.Operation)?.TargetMethod.Name
                )
            )
            .ToArray();

        Assert.Equal(
            new (string, string?, bool, string?)[]
            {
                ("await Task.Delay(1)", "FinallyClauseSyntax", false, "Delay"),
                ("await Task.Delay(2).ConfigureAwait(false)", (string?)null, true, "Delay"),
                ("await Task.Delay(3)", (string?)null, false, "Delay"),
            },
            facts
        );
    }

    [Fact]
    public void Only_unawaited_task_statements_and_true_discards_drop_tasks()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "A",
            [
                new(
                    "A.cs",
                    """
                    using System.Threading.Tasks;
                    class A { async Task Run() {
                        Task.Delay(1);
                        _ = Task.Delay(2);
                        Task kept = Task.Delay(3);
                        kept = Task.Delay(4);
                        await Task.Delay(5);
                    } }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var dropped = Code.Nodes<ExpressionStatementSyntax>()
            .In(solution)
            .Select(node => AsyncFacts.ForgottenTask(node.Syntax, node.Source.Model)?.ToString())
            .ToArray();

        Assert.Equal(["Task.Delay(1)", "Task.Delay(2)", null, null], dropped);
    }
}
