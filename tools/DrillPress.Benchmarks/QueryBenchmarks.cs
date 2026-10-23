using BenchmarkDotNet.Attributes;
using DrillPress.Testing;

namespace DrillPress.Benchmarks;

/// <summary>Evaluates typical rule catalogs over a fresh synthetic solution, so every iteration pays binding again.</summary>
public class QueryBenchmarks
{
    private RuleCatalog _rules = null!;
    private AnalysisSolution _solution = null!;

    [Params(1, 16, 64)]
    public int Rules { get; set; }

    [Params(true, false)]
    public bool Optimized { get; set; }

    [GlobalSetup]
    public void Setup() => _rules = QueryWorkload.Rules(Rules);

    [IterationSetup]
    public void CreateSolution() =>
        _solution = new(
            [QueryWorkload.Project(new RuleTestWorkspace(), 300)],
            new AnalysisOptions { EnableOptimizations = Optimized }
        );

    [Benchmark]
    public int Evaluate() => _rules.Evaluate(_solution).Count;
}
