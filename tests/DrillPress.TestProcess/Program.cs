using System.IO.Abstractions;

var fileSystem = new FileSystem();

if (args is ["bytes"])
{
    await Console.OpenStandardOutput().WriteAsync(Enumerable.Repeat((byte)255, 200_000).ToArray());
    await Console.OpenStandardError().WriteAsync(Enumerable.Repeat((byte)0, 200_000).ToArray());
    return 2;
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
