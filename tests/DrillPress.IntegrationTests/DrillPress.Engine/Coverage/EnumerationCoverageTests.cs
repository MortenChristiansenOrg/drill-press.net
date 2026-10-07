using DrillPress.Engine;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.Engine.Coverage;

public sealed class EnumerationCoverageTests : CoverageIntegrationTest
{
    private const string Source = """
        using System.Collections;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        public static class Loops {
            public static IEnumerable<int> Empty() => System.Array.Empty<int>();
            public static IEnumerable<(int,int)> Pairs() => System.Array.Empty<(int,int)>();
            public static async IAsyncEnumerable<int> AsyncEmpty() { await Task.CompletedTask; yield break; }
            public static void Sync() { foreach (var item in Empty()) { } }
            public static async Task Async() { await foreach (var item in AsyncEmpty()) { } }
            public static void Deconstruct() { foreach (var (a, b) in Pairs()) { } }
            public static IEnumerable<int> FailingCollection() => throw new System.InvalidOperationException();
            public static void CollectionFails() { foreach (var item in FailingCollection()) { } }
            public static void AcquisitionFails() { foreach (var item in (IEnumerable<int>)new Broken()) { } }
            public static void Never() { foreach (var item in Empty()) { } }
            public static async Task NeverAsync() { await foreach (var item in AsyncEmpty()) { } }
            public static void Indexed() { foreach (var item in System.Array.Empty<int>()) { } }
        }
        public sealed class Broken : IEnumerable<int> {
            public IEnumerator<int> GetEnumerator() => throw new System.InvalidOperationException();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
        """;

    [Fact]
    public async Task Empty_sequences_advance_but_collection_acquisition_failures_and_skipped_loops_do_not()
    {
        var snapshot = await CreateCoverageSnapshotAsync(
            Source,
            """
            public class LoopTests {
                [Xunit.Fact] public async System.Threading.Tasks.Task EmptyLoops() {
                    Loops.Sync(); await Loops.Async(); Loops.Deconstruct(); Loops.Indexed();
                    Xunit.Assert.Throws<System.InvalidOperationException>(Loops.CollectionFails);
                    Xunit.Assert.Throws<System.InvalidOperationException>(Loops.AcquisitionFails);
                }
            }
            """
        );
        var rules = new RuleSet();
        rules
            .Rule("ENUM", "Start enumeration.")
            .For(Code.Enumerations)
            .Require(global::DrillPress.Coverage.EnumerationStarted);

        var diagnostics = await new AnalysisEngine().AnalyzeAsync(
            rules,
            snapshot,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            [
                "FailingCollection()",
                "(IEnumerable<int>)new Broken()",
                "Empty()",
                "AsyncEmpty()",
                "System.Array.Empty<int>()",
            ],
            diagnostics.Select(diagnostic =>
                Source.Substring(diagnostic.Location.Start, diagnostic.Location.Length)
            )
        );
        Assert.Equal(
            [
                "enumeration: uncovered",
                "enumeration: uncovered",
                "enumeration: uncovered",
                "enumeration: uncovered",
                "enumeration: unknown (unsupported-enumeration)",
            ],
            diagnostics.Select(diagnostic => diagnostic.Evidence)
        );
        Assert.All(
            diagnostics,
            diagnostic =>
                Assert.Equal(CoverageMetric.Enumeration, Assert.Single(diagnostic.Coverage).Metric)
        );
    }
}
