using System.IO.Abstractions;
using DrillPress.CoverageReader;

if (args is not [var binary, var xml, var version] || string.IsNullOrWhiteSpace(version))
    throw new ArgumentException("Expected a binary report, XML report, and collector version.");
await new BlockReport(new FileSystem()).EnrichAsync(binary, xml, version, CancellationToken.None);
