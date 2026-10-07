using DrillPress.Engine;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.Engine.Coverage;

public sealed class ConditionalCoverageTests : CoverageIntegrationTest
{
    private const string Source = """
        using System;
        using System.Linq.Expressions;
        public static class Target {
            static int Hit() => 1;
            static bool Test() => true;
            static T Identity<T>(T value) => value;
            public static int CoalesceSkipped(int? cached) => cached ?? Hit();
            public static int CoalesceTaken(int? cached) => cached ?? Hit();
            public static int TernarySkipped(bool flag) => flag ? 2 : Hit();
            public static int TernaryTaken(bool flag) => flag ? 2 : Hit();
            public static bool AndSkipped(bool flag) => flag && Test();
            public static bool AndTaken(bool flag) => flag && Test();
            public static bool OrSkipped(bool flag) => flag || Test();
            public static bool OrTaken(bool flag) => flag || Test();
            public static int? AccessSkipped(Receiver? value) => value?.Hit();
            public static int? AccessTaken(Receiver? value) => value?.Hit();
            static int Throwing() => throw new InvalidOperationException();
            static Receiver ThrowingReceiver() => throw new InvalidOperationException();
            static int Huge() => int.MaxValue;
            static void Consume(int first, int second) { }
            public static void EarlierArgumentThrows() { Consume(Throwing(), Hit()); }
            public static void EarlierReceiverThrows() { ThrowingReceiver().Hit(); }
            public static void ConversionThrows() { Consume(checked((sbyte)Huge()), 0); }
            public static Expression<Func<int>> Tree() => () => Hit();
            public static int Duplicate(bool flag) => flag ? Hit() : Hit();
            public static int Generic(bool flag) => flag ? Identity(3) : 0;
            static readonly Store _store = new();
            public static async System.Threading.Tasks.Task<int> AsyncSkipped(int? cached) => cached ?? await _store.LoadAsync();
            public static async System.Threading.Tasks.Task<int> AsyncTaken(int? cached) => cached ?? await _store.LoadAsync();
            public static int DuplicateNever(bool flag) => flag ? Hit() : Hit();
            public static Expression<Func<int>> TreeNever() => () => Hit();
        }
        public sealed class Store { public System.Threading.Tasks.Task<int> LoadAsync() => System.Threading.Tasks.Task.FromResult(9); }
        public sealed class Receiver { public int Hit() => 7; }
        """;

    [Fact]
    public async Task Individual_blocks_prove_selected_calls_and_preserve_skips_and_evaluation_failures()
    {
        var snapshot = await CreateCoverageSnapshotAsync(
            Source,
            """
            public class Cases {
                [Xunit.Fact] public void Skipped() {
                    Xunit.Assert.Equal(5, Target.CoalesceSkipped(5));
                    Xunit.Assert.Equal(2, Target.TernarySkipped(true));
                    Xunit.Assert.False(Target.AndSkipped(false));
                    Xunit.Assert.True(Target.OrSkipped(true));
                    Xunit.Assert.Null(Target.AccessSkipped(null));
                    Xunit.Assert.Equal(1, Target.Duplicate(true));
                }
                [Xunit.Fact] public void Taken() {
                    Xunit.Assert.Equal(1, Target.CoalesceTaken(null));
                    Xunit.Assert.Equal(1, Target.TernaryTaken(false));
                    Xunit.Assert.True(Target.AndTaken(true));
                    Xunit.Assert.True(Target.OrTaken(false));
                    Xunit.Assert.Equal(7, Target.AccessTaken(new Receiver()));
                    Xunit.Assert.Equal(1, Target.Duplicate(false));
                    Xunit.Assert.Equal(3, Target.Generic(true));
                    Xunit.Assert.NotNull(Target.Tree());
                }
                [Xunit.Fact] public async System.Threading.Tasks.Task Awaited() {
                    Xunit.Assert.Equal(5, await Target.AsyncSkipped(5));
                    Xunit.Assert.Equal(9, await Target.AsyncTaken(null));
                }
                [Xunit.Fact] public void FailureBeforeCall() {
                    Xunit.Assert.Throws<System.InvalidOperationException>(Target.EarlierArgumentThrows);
                    Xunit.Assert.Throws<System.InvalidOperationException>(Target.EarlierReceiverThrows);
                    Xunit.Assert.Throws<System.OverflowException>(Target.ConversionThrows);
                }
            }
            """
        );
        RuleCondition<CodeInvocation> executed = global::DrillPress.Coverage.Executed;
        var rules = new RuleCatalog();
        rules
            .Rule("PROBE", "Capture call evidence.")
            .For(
                Code.Calls.Where(call =>
                    call.Target.Name is "Hit" or "Test" or "Consume" or "Identity" or "LoadAsync"
                )
            )
            .Require(executed.And(new(_ => false)));
        var engine = new AnalysisEngine();

        var response = await engine.EvaluateAsync(
            rules,
            snapshot,
            new() { ExplainCoverage = true },
            TestContext.Current.CancellationToken
        );

        var findings = Assert.Single(response.Contexts).Findings;
        Assert.Equal(
            [
                ExecutionCoverage.Uncovered,
                ExecutionCoverage.Covered,
                ExecutionCoverage.Uncovered,
                ExecutionCoverage.Covered,
                ExecutionCoverage.Uncovered,
                ExecutionCoverage.Covered,
                ExecutionCoverage.Uncovered,
                ExecutionCoverage.Covered,
                ExecutionCoverage.Uncovered,
                ExecutionCoverage.Covered,
                ExecutionCoverage.Uncovered,
                ExecutionCoverage.Uncovered,
                ExecutionCoverage.Uncovered,
                ExecutionCoverage.Unknown,
                ExecutionCoverage.Unknown,
                ExecutionCoverage.Unknown,
                ExecutionCoverage.Unknown,
                ExecutionCoverage.Covered,
                ExecutionCoverage.Uncovered,
                ExecutionCoverage.Covered,
                ExecutionCoverage.Unknown,
                ExecutionCoverage.Unknown,
                ExecutionCoverage.Unknown,
            ],
            findings.Select(finding => Assert.Single(finding.Coverage!).State)
        );
        Assert.Equal(
            [
                "Hit()",
                "Hit()",
                "Hit()",
                "Hit()",
                "Test()",
                "Test()",
                "Test()",
                "Test()",
                ".Hit()",
                ".Hit()",
                "Consume(Throwing(), Hit())",
                "Hit()",
                "ThrowingReceiver().Hit()",
                "Consume(checked((sbyte)Huge()), 0)",
                "Hit()",
                "Hit()",
                "Hit()",
                "Identity(3)",
                "_store.LoadAsync()",
                "_store.LoadAsync()",
                "Hit()",
                "Hit()",
                "Hit()",
            ],
            findings.Select(finding => Source.Substring(finding.Start, finding.Length))
        );
        Assert.All(
            findings.Where(finding =>
                Assert.Single(finding.Coverage!).State == ExecutionCoverage.Covered
            ),
            finding =>
                Assert.Contains(
                    Assert.Single(finding.Coverage!).Calls!,
                    call => call.State == ExecutionCoverage.Covered
                )
        );
        Assert.All(
            findings.Where(finding =>
                Assert.Single(finding.Coverage!).State == ExecutionCoverage.Unknown
            ),
            finding =>
                Assert.Equal(
                    [CoverageReason.UnsupportedExpressionMapping],
                    Assert.Single(finding.Coverage!).Reasons
                )
        );
    }
}
