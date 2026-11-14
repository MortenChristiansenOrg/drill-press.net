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
        await FileSystem.File.WriteAllTextAsync(
            project,
            "<Project />",
            TestContext.Current.CancellationToken
        );
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = probeDirectory,
            ["DOTNET_ROOT"] = FileSystem.Path.GetDirectoryName(dotnet)!,
            ["DRILLPRESS_SDK_PROBE_READY"] = ready,
        };

        var host = FileSystem.Path.Combine(
            probeDirectory,
            OperatingSystem.IsWindows() ? "DrillPress.BuildHost.exe" : "DrillPress.BuildHost"
        );

        var run = RunProcessAsync(
            host,
            ["export", project, snapshot],
            RepositoryRoot,
            cancellation.Token,
            environment
        );
        var probe = await WaitForTestProcessAsync(ready, run, cancellation);
        var result = await run;

        Assert.Equal(
            new ProcessResult(
                2,
                "",
                $"DrillPress.BuildHost: SDK discovery timed out after 30 seconds for '{root}'.{Environment.NewLine}"
            ),
            result
        );
        Assert.True(probe.HasExited);
        Assert.False(FileSystem.File.Exists(snapshot));
    }

    // BuildHost runs on a copied .NET root whose dotnet is a test process, so the restart's command line and
    // environment can be read back. Only a preview SDK lets the restart choose a prerelease runtime, whatever
    // BuildHost inherited.
    [Theory]
    [InlineData("99.0.100-rc.1.26101.1", "0", "1")]
    [InlineData("99.0.100", "1", "0")]
    public async Task Sdk_newer_than_the_runtime_restarts_export_on_the_newest_runtime(
        string sdkVersion,
        string inheritedPrerelease,
        string restartPrerelease
    )
    {
        var root = CreateTemporaryDirectory("drillpress-sdk-restart-").FullName;
        var probeDirectory = CreateProbeDirectory(root);
        var dotnetRoot = CreateDotNetRoot(root, probeDirectory);
        var log = FileSystem.Path.Combine(root, "restart.log");
        var project = FileSystem.Path.Combine(root, "Target.csproj");
        var snapshot = FileSystem.Path.Combine(root, "snapshot.json");
        await FileSystem.File.WriteAllTextAsync(
            project,
            "<Project />",
            TestContext.Current.CancellationToken
        );
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = probeDirectory,
            ["DOTNET_ROOT"] = dotnetRoot,
            // dotnet test sets the architecture's own variable, which takes precedence.
            [
                $"DOTNET_ROOT_{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant()}"
            ] = dotnetRoot,
            ["DOTNET_ROLL_FORWARD_TO_PRERELEASE"] = inheritedPrerelease,
            ["DRILLPRESS_SDK_PROBE_VERSION"] = sdkVersion,
            ["DRILLPRESS_RESTART_LOG"] = log,
        };
        var host = FileSystem.Path.Combine(
            probeDirectory,
            OperatingSystem.IsWindows() ? "DrillPress.BuildHost.exe" : "DrillPress.BuildHost"
        );

        var result = await RunProcessAsync(
            host,
            ["export", project, snapshot, "--validate-compilation"],
            RepositoryRoot,
            TestContext.Current.CancellationToken,
            environment
        );

        Assert.Equal(new ProcessResult(3, "", ""), result);
        Assert.Equal(
            [
                "exec",
                "--roll-forward",
                "LatestMajor",
                FileSystem.Path.Combine(probeDirectory, "DrillPress.BuildHost.dll"),
                "export",
                project,
                snapshot,
                "--validate-compilation",
                $"DOTNET_ROLL_FORWARD_TO_PRERELEASE={restartPrerelease}",
                "DRILLPRESS_BUILDHOST_ROLLED_FORWARD=1",
            ],
            await FileSystem.File.ReadAllLinesAsync(log, TestContext.Current.CancellationToken)
        );
    }

    // Copies this test's runtime and host resolver into a new .NET root, with the test process as its dotnet.
    private static string CreateDotNetRoot(string root, string probeDirectory)
    {
        var runtime = FileSystem.DirectoryInfo.New(
            System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()
        );
        var installation = runtime.Parent!.Parent!.Parent!;
        var dotnetRoot = FileSystem.Path.Combine(root, "dotnet");
        Copy(
            runtime.FullName,
            FileSystem.Path.Combine(dotnetRoot, "shared", "Microsoft.NETCore.App", runtime.Name)
        );
        Copy(
            FileSystem.Path.Combine(installation.FullName, "host"),
            FileSystem.Path.Combine(dotnetRoot, "host")
        );
        foreach (
            var path in FileSystem.Directory.EnumerateFiles(
                FileSystem.Path.GetDirectoryName(GetOutputPath("DrillPress.TestProcess", "tests"))!
            )
        )
        {
            FileSystem.File.Copy(
                path,
                FileSystem.Path.Combine(dotnetRoot, FileSystem.Path.GetFileName(path))
            );
        }

        var extension = OperatingSystem.IsWindows() ? ".exe" : "";
        FileSystem.File.Copy(
            FileSystem.Path.Combine(probeDirectory, "dotnet" + extension),
            FileSystem.Path.Combine(dotnetRoot, "dotnet" + extension)
        );
        return dotnetRoot;
    }

    private static void Copy(string source, string destination)
    {
        FileSystem.Directory.CreateDirectory(destination);
        foreach (
            var directory in FileSystem.Directory.EnumerateDirectories(
                source,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            FileSystem.Directory.CreateDirectory(
                FileSystem.Path.Combine(
                    destination,
                    FileSystem.Path.GetRelativePath(source, directory)
                )
            );
        }

        foreach (
            var file in FileSystem.Directory.EnumerateFiles(
                source,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            FileSystem.File.Copy(
                file,
                FileSystem.Path.Combine(destination, FileSystem.Path.GetRelativePath(source, file))
            );
        }
    }

    private static string CreateProbeDirectory(string root)
    {
        var directory = FileSystem
            .Directory.CreateDirectory(FileSystem.Path.Combine(root, "probe"))
            .FullName;
        foreach (
            var (project, parent) in new[]
            {
                ("DrillPress.TestProcess", "tests"),
                ("DrillPress.BuildHost", "src"),
            }
        )
        {
            var source = FileSystem.Path.GetDirectoryName(GetOutputPath(project, parent))!;
            foreach (var path in FileSystem.Directory.EnumerateFiles(source))
            {
                FileSystem.File.Copy(
                    path,
                    FileSystem.Path.Combine(directory, FileSystem.Path.GetFileName(path)),
                    true
                );
            }
        }

        var extension = OperatingSystem.IsWindows() ? ".exe" : "";
        FileSystem.File.Copy(
            FileSystem.Path.Combine(directory, "DrillPress.TestProcess" + extension),
            FileSystem.Path.Combine(directory, "dotnet" + extension)
        );
        return directory;
    }

    private static string FindDotNet()
    {
        var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var path = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(FileSystem.Path.PathSeparator)
            .Select(directory => FileSystem.Path.Combine(directory.Trim('"'), name))
            .First(FileSystem.File.Exists);
        return FileSystem.FileInfo.New(path).ResolveLinkTarget(true)?.FullName
            ?? FileSystem.Path.GetFullPath(path);
    }
}
