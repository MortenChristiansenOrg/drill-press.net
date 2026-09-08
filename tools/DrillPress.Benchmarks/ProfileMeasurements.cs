using System.IO.Abstractions;
using System.Text.Json;
using DrillPress.Manifest;

namespace DrillPress.Benchmarks;

public sealed class ProfileMeasurements(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private const string Prefix = "drillpress-profile ";

    public ProfileEvent[] Read(string path, double outerWallMilliseconds, params string[] requiredComponents)
    {
        var events = Read(path, requiredComponents);
        if (!double.IsFinite(outerWallMilliseconds) || outerWallMilliseconds < 0 ||
            events.Any(entry => entry.Phase == "total" && entry.WallMilliseconds > outerWallMilliseconds))
        {
            throw new InvalidDataException("A process profile total exceeds its matching outer Stopwatch interval.");
        }

        return events;
    }

    public ProfileEvent[] Read(string path, params string[] requiredComponents)
    {
        var events = _fileSystem.File.ReadLines(path).Where(line => line.StartsWith(Prefix, StringComparison.Ordinal))
            .Select(line => JsonSerializer.Deserialize(line[Prefix.Length..], CompilationSnapshotJsonContext.Default.ProfileEvent)
                ?? throw new InvalidDataException("A profile event was missing.")).ToArray();
        if (events.Any(entry => !double.IsFinite(entry.WallMilliseconds) || entry.WallMilliseconds < 0 ||
            !double.IsFinite(entry.UserCpuMilliseconds) || entry.UserCpuMilliseconds < 0 ||
            !double.IsFinite(entry.SystemCpuMilliseconds) || entry.SystemCpuMilliseconds < 0 || entry.ProcessId <= 0 ||
            string.IsNullOrWhiteSpace(entry.Component) || string.IsNullOrWhiteSpace(entry.Phase) ||
            entry.ProcessPeakWorkingSetBytes < 0 || entry.Count is < 0) ||
            requiredComponents.Any(component => !events.Any(entry => entry.Component == component && entry.Phase == "total" && entry.Count is null)))
        {
            throw new InvalidDataException("Profiling did not provide complete, valid process totals.");
        }

        return events;
    }
}
