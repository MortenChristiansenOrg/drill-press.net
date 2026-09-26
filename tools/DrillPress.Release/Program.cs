using System.IO.Abstractions;
using DrillPress.Release;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
try
{
    if (args.Length is < 2 or > 3 || args[0] is not ("version" or "validate" or "publish"))
    {
        throw new ArgumentException(
            "Usage: DrillPress.Release version TAG | validate TAG DIRECTORY | publish TAG DIRECTORY"
        );
    }

    var version = ReleaseVersion.FromTag(args[1]);
    if (args[0] == "version" && args.Length == 2)
    {
        Console.WriteLine(version.Version);
        return (int)ReleaseExitCode.Success;
    }

    if (args.Length != 3 || args[0] == "version")
    {
        throw new ArgumentException("Package directory required for validate/publish.");
    }

    var packages = new PackageInventory(new FileSystem(), new AssemblyVersionReader()).Read(
        args[2],
        version
    );
    if (args[0] == "publish")
    {
        using var client = new HttpClient();
        var key = Environment.GetEnvironmentVariable("NUGET_API_KEY") ?? "";
        await new ReleasePublisher(new NugetFeed(client)).PublishAsync(
            packages,
            key,
            cancellation.Token
        );
    }

    Console.WriteLine($"{args[0]}: {packages.Count} packages at {version.Version}");
    return (int)ReleaseExitCode.Success;
}
catch (Exception exception)
    when (exception
            is ArgumentException
                or InvalidOperationException
                or IOException
                or HttpRequestException
                or OperationCanceledException
    )
{
    Console.Error.WriteLine(exception.Message);
    return (int)ReleaseExitCode.Failure;
}
