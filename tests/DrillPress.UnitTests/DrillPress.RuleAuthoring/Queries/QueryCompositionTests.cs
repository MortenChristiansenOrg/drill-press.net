using DrillPress.Facts;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Queries;

public sealed class QueryCompositionTests
{
    [Fact]
    public void Derived_queries_share_discovery_and_keep_solution_lifetimes_separate()
    {
        var discoveries = 0;
        var root = CodeQuery<int>.Create(_ =>
        {
            discoveries++;
            return [1, 2, 3];
        });
        var small = root.Where(value => value <= 2);
        var large = root.Where(value => value >= 2);
        var first = new AnalysisSolution([]);
        var second = new AnalysisSolution([]);

        var result = new[] { small.In(first), large.In(first), small.In(first), small.In(second) };

        Assert.Equal(2, discoveries);
        Assert.Equal<int>([1, 2], result[0]);
        Assert.Equal<int>([2, 3], result[1]);
        Assert.Same(result[0], result[2]);
        Assert.Equal<int>([1, 2], result[3]);
    }

    [Fact]
    public void Counterpart_requirements_and_joins_use_explicit_equality()
    {
        var expected = CodeQuery<string>.Create(_ => ["Json", "Xml"]);
        var actual = CodeQuery<string>.Create(_ => ["JSON"]);
        var missing = expected.WithoutMatching(
            actual,
            item => item,
            item => item,
            StringComparer.OrdinalIgnoreCase
        );
        var joined = expected.Join(
            actual,
            item => item,
            item => item,
            (left, right) => left + ":" + right,
            StringComparer.OrdinalIgnoreCase
        );
        var solution = new AnalysisSolution([]);

        var missingResult = missing.In(solution);
        var joinedResult = joined.In(solution);

        Assert.Equal<string>(["Xml"], missingResult);
        Assert.Equal<string>(["Json:JSON"], joinedResult);
    }

    [Fact]
    public void Facts_compose_with_queries_and_compute_once()
    {
        var computations = 0;
        var fact = new AnalysisFact<int>(_ => ++computations);
        var query = fact.SelectMany(value => new[] { value, value + 1 });
        var solution = new AnalysisSolution([]);

        var first = query.In(solution);
        var value = fact.In(solution);

        Assert.Equal<int>([1, 2], first);
        Assert.Equal(1, value);
        Assert.Equal(1, computations);
    }

    [Fact]
    public void Cancelled_analysis_does_not_return_previously_cached_results()
    {
        using var cancellation = new CancellationTokenSource();
        var solution = new AnalysisSolution([], cancellation.Token);
        var query = CodeQuery<int>.Create(_ => [1]);
        query.In(solution);
        cancellation.Cancel();

        var error = Assert.Throws<OperationCanceledException>(() => query.In(solution));

        Assert.Equal(cancellation.Token, error.CancellationToken);
    }
}
