using System.Globalization;
using DrillPress.BundleVerification;

namespace DrillPress.Benchmarks;

public static class LinuxMemoryProbe
{
    public static async Task<MemorySample> RunAsync(BenchmarkPlan plan, BundleMode mode, BundleCase scenario)
    {
        // A managed parent can inflate ru_maxrss through its pre-exec fork image.
        // GNU time forks from its small native image and reports only that child's
        // high-water mark. Its timing fields are deliberately not requested.
        var directory = Directory.CreateTempSubdirectory("drillpress-linux-memory-");
        try
        {
            var peakPath = Path.Combine(directory.FullName, "peak-kib");
            string[] target = mode switch
            {
                BundleMode.Managed => ["dotnet", plan.ManagedBundle, .. scenario.Arguments],
                BundleMode.Native => [plan.NativeBundle, .. scenario.Arguments],
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
            var output = await ProcessRunner.RunAsync("/usr/bin/time",
                ["--quiet", "-f", "%M", "-o", peakPath, "--", .. target], plan.RepositoryRoot);
            BundleContract.Validate(scenario, output);
            var peakBytes = checked(long.Parse(await File.ReadAllTextAsync(peakPath),
                CultureInfo.InvariantCulture) * 1024);
            if (peakBytes <= 0)
            {
                throw new InvalidOperationException("GNU time did not report positive child peak memory.");
            }

            return new MemorySample(mode, scenario.Name, peakBytes, output);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
