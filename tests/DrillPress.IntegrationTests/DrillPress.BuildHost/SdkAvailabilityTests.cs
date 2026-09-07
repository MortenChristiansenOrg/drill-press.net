using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.BuildHost;

public sealed class SdkAvailabilityTests : IntegrationTest
{
    [Fact]
    public async Task Stalled_sdk_probe_is_terminated_before_export_reports_its_deadline()
    {
        var root = CreateTemporaryDirectory("drillpress-sdk-timeout-").FullName;
        var probeDirectory = CreateProbeDirectory(root);
        var dotnet = FindDotNet();
        var ready = FileSystem.Path.Combine(root, "ready");
        var project = FileSystem.Path.Combine(root, "Target.csproj");
        var snapshot = FileSystem.Path.Combine(root, "snapshot.json");
        await FileSystem.File.WriteAllTextAsync(project, "<Project />", TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = probeDirectory,
            ["DOTNET_ROOT"] = FileSystem.Path.GetDirectoryName(dotnet)!,
            ["DRILLPRESS_SDK_PROBE_READY"] = ready,
        };

        var host = FileSystem.Path.Combine(probeDirectory, OperatingSystem.IsWindows() ? "DrillPress.BuildHost.exe" : "DrillPress.BuildHost");

        var run = RunProcessAsync(host, ["export", project, snapshot],
            RepositoryRoot, cancellation.Token, environment);
        var probe = await WaitForTestProcessAsync(ready, run, cancellation);
        var result = await run;

        Assert.Equal(new ProcessResult(2, "", $"DrillPress.BuildHost: SDK discovery timed out after 30 seconds for '{root}'.{Environment.NewLine}"), result);
        Assert.True(probe.HasExited);
        Assert.False(FileSystem.File.Exists(snapshot));
    }

    private static string CreateProbeDirectory(string root)
    {
        var directory = FileSystem.Directory.CreateDirectory(FileSystem.Path.Combine(root, "probe")).FullName;
        foreach (var (project, parent) in new[] { ("DrillPress.TestProcess", "tests"), ("DrillPress.BuildHost", "src") })
        {
            var source = FileSystem.Path.GetDirectoryName(GetOutputPath(project, parent))!;
            foreach (var path in FileSystem.Directory.EnumerateFiles(source))
            {
                FileSystem.File.Copy(path, FileSystem.Path.Combine(directory, FileSystem.Path.GetFileName(path)), true);
            }
        }

        var extension = OperatingSystem.IsWindows() ? ".exe" : "";
        FileSystem.File.Copy(FileSystem.Path.Combine(directory, "DrillPress.TestProcess" + extension), FileSystem.Path.Combine(directory, "dotnet" + extension));
        return directory;
    }

    private static string FindDotNet()
    {
        var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(FileSystem.Path.PathSeparator)
            .Select(directory => FileSystem.Path.Combine(directory.Trim('"'), name)).First(FileSystem.File.Exists);
        return FileSystem.FileInfo.New(path).ResolveLinkTarget(true)?.FullName ?? FileSystem.Path.GetFullPath(path);
    }
}
