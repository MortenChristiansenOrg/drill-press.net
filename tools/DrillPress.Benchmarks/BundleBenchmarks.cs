using System.IO.Abstractions;
using BenchmarkDotNet.Attributes;
using DrillPress.BundleVerification;

namespace DrillPress.Benchmarks;

public class BundleBenchmarks
{
    public const string PlanEnvironmentVariable = "DRILLPRESS_BENCHMARK_PLAN";
    private BenchmarkPlan plan = null!;
    private BundleCase scenario = null!;

    [Params(BundleMode.Managed, BundleMode.Native)]
    public BundleMode Mode { get; set; }

    [Params("startup", "clean", "violating", "invalid")]
    public string Case { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        plan = new BenchmarkPlanFile(new FileSystem()).Read(
            Environment.GetEnvironmentVariable(PlanEnvironmentVariable)
            ?? throw new InvalidOperationException("A verified benchmark plan is required."));
        scenario = plan.Cases.Single(item => item.Name == Case);
    }

    [Benchmark]
    public Task<ProcessOutput> Execute() => BenchmarkExecution.RunAsync(plan, Mode, scenario);
}
