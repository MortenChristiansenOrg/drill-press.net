using System.Runtime.InteropServices;
using System.Text;

namespace DrillPress.BundleVerification;

public sealed class VerificationSession : IDisposable
{
    private readonly DirectoryInfo _fixture = Directory.CreateTempSubdirectory("drillpress-native-");

    private VerificationSession(string root, string output, string rid)
    {
        RepositoryRoot = root;
        OutputDirectory = output;
        RuntimeIdentifier = rid;
    }

    public string RepositoryRoot { get; }
    public string OutputDirectory { get; }
    public string RuntimeIdentifier { get; }
    public string ManagedBundle => Path.Combine(OutputDirectory, "managed", "DrillPress.SampleRules.dll");
    public string NativeBundle => Path.Combine(OutputDirectory, "native",
        OperatingSystem.IsWindows() ? "DrillPress.SampleRules.exe" : "DrillPress.SampleRules");
    public BundleCase[] Cases { get; private set; } = [];

    public static async Task<VerificationSession> CreateAsync(string? output = null)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DrillPress.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? throw new InvalidOperationException("Run from the repository directory.");
        if (RuntimeInformation.OSArchitecture != Architecture.X64 ||
            !(OperatingSystem.IsLinux() || OperatingSystem.IsWindows()))
        {
            throw new PlatformNotSupportedException("Supported runtime identifiers are linux-x64 and win-x64.");
        }

        var rid = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        output = Path.GetFullPath(output ?? Path.Combine(root, "artifacts", $"native-{rid}-{Guid.NewGuid():N}"));
        if (Directory.Exists(output) || File.Exists(output))
        {
            throw new IOException($"Report destination already exists: {output}");
        }

        Directory.CreateDirectory(output);
        var session = new VerificationSession(root, output, rid);
        try
        {
            await session.PublishAsync();
            await session.CreateCasesAsync();
            await session.VerifyAsync();
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public async Task<ProcessOutput> ExecuteAsync(BundleMode mode, BundleCase @case)
    {
        var result = await ProcessRunner.RunAsync(
            mode == BundleMode.Managed ? "dotnet" : NativeBundle,
            mode == BundleMode.Managed ? [ManagedBundle, .. @case.Arguments] : @case.Arguments,
            RepositoryRoot);
        BundleContract.Validate(@case, result);
        return result;
    }

    public void Dispose() => _fixture.Delete(recursive: true);

    private async Task PublishAsync()
    {
        const string sample = "samples/DrillPress.SampleRules/DrillPress.SampleRules.csproj";
        await BuildCommandAsync("build.log", ["build", "DrillPress.slnx", "-c", "Release"]);
        await BuildCommandAsync("managed-publish.log",
            ["publish", sample, "-c", "Release", "-p:PublishAot=false", "--self-contained", "false", "-o", Path.Combine(OutputDirectory, "managed")]);
        var publication = await BuildCommandAsync("publish.log",
            ["publish", sample, "-c", "Release", "-r", RuntimeIdentifier, "-o", Path.Combine(OutputDirectory, "native")]);
        PublishWarnings.Validate(Encoding.UTF8.GetString(publication.StandardOutput) + Encoding.UTF8.GetString(publication.StandardError));
    }

    private async Task<ProcessOutput> BuildCommandAsync(string log, string[] arguments)
    {
        var result = await ProcessRunner.RunAsync("dotnet", arguments, RepositoryRoot, timeout: TimeSpan.FromMinutes(10));
        await File.WriteAllBytesAsync(Path.Combine(OutputDirectory, log), [.. result.StandardOutput, .. result.StandardError]);
        ProcessRunner.RequireSuccess(result);
        return result;
    }

    private string BuildHost => Path.Combine(RepositoryRoot, "src/DrillPress.BuildHost/bin/Release/net10.0/DrillPress.BuildHost.dll");

    private async Task CreateCasesAsync()
    {
        var project = Path.Combine(_fixture.FullName, "Probe.csproj");
        var source = Path.Combine(_fixture.FullName, "Probe.cs");
        await File.WriteAllTextAsync(project,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <LangVersion>14.0</LangVersion>
              </PropertyGroup>
            </Project>
            """);
        var cases = new List<BundleCase>();
        foreach (var (name, expression, outcome) in new[]
                 { ("clean", "\"\"", BundleOutcome.Clean), ("violating", "Text.Empty", BundleOutcome.Findings) })
        {
            await File.WriteAllTextAsync(source, $$"""
                using Text = System.String;
                public static class Probe
                {
                    public static string Value => {{expression}};
                }
                """);
            var snapshot = Path.Combine(_fixture.FullName, $"{name}.json");
            ProcessRunner.RequireSuccess(await ProcessRunner.RunAsync("dotnet", [BuildHost, "export", project, snapshot], RepositoryRoot));
            var stdout = outcome == BundleOutcome.Clean ? [] : Encoding.UTF8.GetBytes(
                string.Join(Environment.NewLine,
                    "DP1004 Use the empty string literal \"\" instead of string.Empty.",
                    Path.GetRelativePath(RepositoryRoot, source).Replace('\\', '/'), "  4:35", ""));
            cases.Add(new BundleCase(name, ["check", snapshot], outcome, stdout, []));
        }

        var invalid = Path.Combine(_fixture.FullName, "invalid.json");
        await File.WriteAllTextAsync(invalid,
            """{"fileIdentifier":"drillpress-compilation","formatVersion":-1,"projects":[]}""");
        cases.Add(new BundleCase("invalid", ["check", invalid], BundleOutcome.Failure, [], Encoding.UTF8.GetBytes(
            "drillpress-rules: Compilation snapshot format -1 is not supported; expected 1." + Environment.NewLine)));
        Cases = cases.ToArray();
    }

    private async Task VerifyAsync()
    {
        foreach (var @case in Cases)
        {
            foreach (var mode in Enum.GetValues<BundleMode>())
            {
                var result = await ExecuteAsync(mode, @case);
                var prefix = Path.Combine(OutputDirectory, $"{@case.Name}.{mode}");
                await File.WriteAllBytesAsync(prefix + ".stdout", result.StandardOutput);
                await File.WriteAllBytesAsync(prefix + ".stderr", result.StandardError);
            }

            Console.WriteLine($"{@case.Name}: managed/native bytes and exit code {(int)@case.Outcome} match");
        }

        var cli = Path.Combine(RepositoryRoot, "src/DrillPress.Cli/bin/Release/net10.0/DrillPress.Cli.dll");
        var resultCli = await ProcessRunner.RunAsync("dotnet",
            [cli, "check", "--build-host", BuildHost, "--rules", NativeBundle, Path.Combine(_fixture.FullName, "Probe.csproj")], RepositoryRoot);
        BundleContract.Validate(Cases.Single(@case => @case.Name == "violating"), resultCli);
        Console.WriteLine("CLI/native: complete BuildHost-to-native path matches");
    }
}
