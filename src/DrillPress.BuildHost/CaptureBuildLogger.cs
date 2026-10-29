using System.Collections.Concurrent;
using Microsoft.Build.Framework;

namespace DrillPress.BuildHost;

internal sealed class CaptureBuildLogger : ILogger
{
    internal ConcurrentQueue<string> Errors { get; } = new();
    public LoggerVerbosity Verbosity { get; set; }
    public string? Parameters { get; set; }

    public void Initialize(IEventSource eventSource) =>
        eventSource.ErrorRaised += (_, error) => Errors.Enqueue(error.Message ?? "MSBuild failed.");

    public void Shutdown() { }
}
