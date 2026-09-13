#:project ../tools/DrillPress.Benchmarks/DrillPress.Benchmarks.csproj
#:property PublishAot=false

using System.IO.Abstractions;
using DrillPress.BuildHost;
using DrillPress.Conformance;
using DrillPress.Engine;
using DrillPress.Manifest;

var fileSystem = new FileSystem();
if (
    args.Length is not (2 or 4)
    || (
        args.Length == 4
        && (args[2] != "--performance" || !int.TryParse(args[3], out var count) || count < 1)
    )
)
{
    Console.Error.WriteLine(
        "Usage: dotnet run --file scripts/XunitConformance.cs -- <checkout> <report-directory> [--performance <repetitions>]"
    );
    return 2;
}

var checkout = args[0];
var output = args[1];

try
{
    if (args.Length == 4)
    {
        await new DrillPress.Benchmarks.RepositoryBenchmark(fileSystem).RunAsync(
            checkout,
            output,
            int.Parse(args[3]),
            CancellationToken.None
        );
        return 0;
    }

    var target = await new PinnedXunit(fileSystem).PrepareAsync(
        checkout,
        fileSystem.Path.Combine(output, "preparation.json"),
        CancellationToken.None
    );
    return (int)
        await new ConformanceApplication(
            fileSystem,
            new MsBuildSnapshotLoader(),
            new AnalysisEngine(),
            new CompilationSnapshotFile()
        ).RunAsync(
            [target, fileSystem.Path.Combine(output, "conformance.json")],
            displayTarget: "<xunit>/xunit.slnx"
        );
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}
