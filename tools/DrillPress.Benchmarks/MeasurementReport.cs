using System.IO.Abstractions;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using DrillPress.BundleVerification;

namespace DrillPress.Benchmarks;

public static class MeasurementReport
{
    public static async Task WriteAsync(
        IFileSystem fileSystem,
        VerificationSession session,
        MemorySample[] memory
    )
    {
        var artifacts = new ArtifactMeasurements(fileSystem);
        var report = new
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription,
            session.RuntimeIdentifier,
            Sdk = await ReadCommandAsync(session, "dotnet", ["--version"]),
            Revision = await ReadCommandAsync(session, "git", ["rev-parse", "HEAD"]),
            DirtyWorktree = (
                await ReadCommandAsync(session, "git", ["status", "--porcelain"])
            ).Length != 0,
            BenchmarkDotNet = typeof(BenchmarkAttribute).Assembly.GetName().Version?.ToString(),
            Timing = "BenchmarkDotNet Monitoring: one launch, one warmup, five single-invocation iterations; "
                + "fresh bundle processes, exact byte/exit checks, warm filesystem caches. See BenchmarkDotNet/results.",
            Startup = "No-argument usage/exit proxy; includes static rule construction, excludes snapshot loading.",
            Memory = "Separate untimed fresh C# worker per child: Linux GNU time %M KiB converted to bytes "
                + "avoids a managed launcher's inherited fork-memory floor; "
                + "Windows GetProcessMemoryInfo.PeakWorkingSetSize. Not BenchmarkDotNet harness allocations.",
            Size = "Dedicated publish directories excluding pdb/dbg/xml; external runtimes/system libraries excluded.",
            Artifacts = new[]
            {
                artifacts.Read(
                    BundleMode.Managed,
                    fileSystem.Path.Combine(session.OutputDirectory, "managed")
                ),
                artifacts.Read(
                    BundleMode.Native,
                    fileSystem.Path.Combine(session.OutputDirectory, "native")
                ),
            },
            PublicOutputBytes = session.PublicOutput.Length,
            EstimatedPublicTokens = (session.PublicOutput.Length + 3) / 4,
            MemorySamples = memory,
        };
        await fileSystem.File.WriteAllTextAsync(
            fileSystem.Path.Combine(session.OutputDirectory, "report.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })
        );
    }

    private static async Task<string> ReadCommandAsync(
        VerificationSession session,
        string executable,
        string[] arguments
    )
    {
        var result = await ProcessRunner.RunAsync(executable, arguments, session.RepositoryRoot);
        ProcessRunner.RequireSuccess(result);
        return Encoding.UTF8.GetString(result.StandardOutput).Trim();
    }
}
