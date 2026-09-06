if (args is ["bytes"])
{
    await Console.OpenStandardOutput().WriteAsync(Enumerable.Repeat((byte)255, 200_000).ToArray());
    await Console.OpenStandardError().WriteAsync(Enumerable.Repeat((byte)0, 200_000).ToArray());
    return 2;
}

if (args is not ["export", var readyPath, var snapshotPath])
{
    return 2;
}

await File.WriteAllTextAsync(readyPath + ".snapshot", snapshotPath);
await File.WriteAllTextAsync(readyPath + ".pending", Environment.ProcessId.ToString());
File.Move(readyPath + ".pending", readyPath);
await Task.Delay(Timeout.InfiniteTimeSpan);
return 0;
