using System.IO.Abstractions;

var fileSystem = new FileSystem();

if (
    args is ["--version"]
    && Environment.GetEnvironmentVariable("DRILLPRESS_SDK_PROBE_READY") is { } sdkReadyPath
)
{
    await fileSystem.File.WriteAllTextAsync(
        sdkReadyPath + ".pending",
        Environment.ProcessId.ToString()
    );
    fileSystem.File.Move(sdkReadyPath + ".pending", sdkReadyPath);
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return 0;
}

if (args is ["bytes"])
{
    await Console.OpenStandardOutput().WriteAsync(Enumerable.Repeat((byte)255, 200_000).ToArray());
    await Console.OpenStandardError().WriteAsync(Enumerable.Repeat((byte)0, 200_000).ToArray());
    return 2;
}

if (args is ["msbuild", var coverageProject, var discoverTests])
{
    var test =
        bool.Parse(discoverTests)
        && fileSystem.Path.GetFileNameWithoutExtension(coverageProject).EndsWith("Tests");
    Console.Write(
        System.Text.Json.JsonSerializer.Serialize(
            new
            {
                Properties = new
                {
                    IsTestProject = test ? "true" : "false",
                    TargetFramework = "net10.0",
                    TargetFrameworks = "",
                    MSBuildAllProjects = "",
                },
                Items = new
                {
                    ProjectReference = test
                        ? new[]
                        {
                            new
                            {
                                FullPath = fileSystem.Path.Combine(
                                    fileSystem.Path.GetDirectoryName(coverageProject)!,
                                    "Target.csproj"
                                ),
                            },
                        }
                        : [],
                    PackageReference = Array.Empty<object>(),
                    None = Enumerable
                        .Range(0, 1200)
                        .Select(index => new
                        {
                            Identity = index.ToString(),
                            Description = new string('x', 1024),
                        })
                        .ToArray(),
                },
            }
        )
    );
    return 0;
}

if (args is ["collect"])
{
    await Task.WhenAll(
        Console.Out.WriteAsync(new string('x', 2 * 1024 * 1024)),
        Console.Error.WriteAsync(new string('y', 2 * 1024 * 1024))
    );
    return 0;
}

if (args is ["limited-output", var pipe])
{
    var stream = pipe == "stdout" ? Console.OpenStandardOutput() : Console.OpenStandardError();
    var buffer = new byte[8192];
    while (true)
    {
        await stream.WriteAsync(buffer);
    }
}

if (args is not ["export", var readyPath, var snapshotPath])
{
    return 2;
}

await fileSystem.File.WriteAllTextAsync(readyPath + ".snapshot", snapshotPath);
await fileSystem.File.WriteAllTextAsync(readyPath + ".pending", Environment.ProcessId.ToString());
fileSystem.File.Move(readyPath + ".pending", readyPath);
await Task.Delay(Timeout.InfiniteTimeSpan);
return 0;
