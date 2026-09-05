if (args is not ["export", var readyPath, var snapshotPath])
{
    return 2;
}

await File.WriteAllTextAsync(readyPath + ".snapshot", snapshotPath);
await File.WriteAllTextAsync(readyPath + ".pending", Environment.ProcessId.ToString());
File.Move(readyPath + ".pending", readyPath);
await Task.Delay(Timeout.InfiniteTimeSpan);
return 0;
