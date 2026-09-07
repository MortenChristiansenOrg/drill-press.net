#:project ../tools/DrillPress.Conformance/DrillPress.Conformance.csproj
#:property PublishAot=false

using System.IO.Abstractions;
using DrillPress.BuildHost;
using DrillPress.Conformance;
using DrillPress.Engine;
using DrillPress.Manifest;

var fileSystem = new FileSystem();
if (args is not [var checkout, var output])
{
    Console.Error.WriteLine("Usage: dotnet run --file scripts/XunitConformance.cs -- <checkout> <report-directory>");
    return 2;
}

try
{
    var target = await new PinnedXunit(fileSystem).PrepareAsync(checkout,
        fileSystem.Path.Combine(output, "preparation.json"), CancellationToken.None);
    return (int)await new ConformanceApplication(fileSystem, new MsBuildSnapshotLoader(), new AnalysisEngine(), new CompilationSnapshotFile())
        .RunAsync([target, fileSystem.Path.Combine(output, "conformance.json")]);
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}
