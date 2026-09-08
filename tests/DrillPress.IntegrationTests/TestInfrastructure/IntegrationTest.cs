using System.Diagnostics;
using System.IO.Abstractions;
using System.Reflection;

namespace DrillPress.IntegrationTests.TestInfrastructure;

public abstract class IntegrationTest : IDisposable
{
    protected static IFileSystem FileSystem { get; } = new FileSystem();

    private readonly List<IDirectoryInfo> _temporaryDirectories = [];
    private readonly List<Process> _testProcesses = [];

    private static string BuildConfiguration { get; } = typeof(IntegrationTest).Assembly
        .GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;

    protected static string RepositoryRoot { get; } = FindRepositoryRoot();

    protected static string SampleProjectPath { get; } = RepositoryPath(
        "Sample Solution",
        "src",
        "WidgetLibrary",
        "WidgetLibrary.csproj");

    public void Dispose()
    {
        foreach (var process in _testProcesses)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }

            process.Dispose();
        }

        foreach (var temporaryDirectory in _temporaryDirectories)
        {
            temporaryDirectory.Delete(recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    protected async Task RestoreAsync(string target)
    {
        var result = await RunProcessAsync("dotnet", ["restore", target, "--nologo"], RepositoryRoot, Xunit.TestContext.Current.CancellationToken);
        Xunit.Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
    }

    protected IDirectoryInfo CreateTemporaryDirectory(string prefix)
    {
        var directory = FileSystem.Directory.CreateTempSubdirectory(prefix);
        _temporaryDirectories.Add(directory);
        return directory;
    }

    protected static string RepositoryPath(params string[] segments) =>
        FileSystem.Path.Combine(RepositoryRoot, FileSystem.Path.Combine(segments));

    protected static string GetOutputPath(string projectName, string projectDirectory = "src") =>
        RepositoryPath(projectDirectory, projectName, "bin", BuildConfiguration, "net10.0", $"{projectName}.dll");

    protected static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                startInfo.Environment[name] = value;
            }
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{fileName}'.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(standardOutput, standardError).WaitAsync(cancellationToken);
            return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(standardOutput, standardError);
            throw;
        }
    }

    protected async Task<Process> WaitForTestProcessAsync(
        string readyPath,
        Task launch,
        CancellationTokenSource cancellation,
        TimeSpan? readinessTimeout = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        timeout.CancelAfter(readinessTimeout ?? TimeSpan.FromSeconds(30));
        try
        {
            while (!FileSystem.File.Exists(readyPath))
            {
                if (launch.IsCompleted)
                {
                    await launch;
                    throw new InvalidOperationException("The test process exited before reporting readiness.");
                }

                await Task.Delay(20, timeout.Token);
            }

            var process = Process.GetProcessById(int.Parse(await FileSystem.File.ReadAllTextAsync(readyPath, timeout.Token)));
            _testProcesses.Add(process);
            return process;
        }
        catch
        {
            await cancellation.CancelAsync();
            try
            {
                await launch;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // The launch task has finished stopping its child process.
            }

            throw;
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = FileSystem.DirectoryInfo.New(AppContext.BaseDirectory);
        while (directory is not null && !FileSystem.File.Exists(FileSystem.Path.Combine(directory.FullName, "DrillPress.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    protected async Task<CliResult> RunCliAsync(string target, params string[] options)
    {
        var temporaryRoot = CreateTemporaryDirectory("drillpress-cli-test-");
        var cli = GetOutputPath("DrillPress.Cli");
        var buildHost = GetOutputPath("DrillPress.BuildHost");
        var rules = GetOutputPath("DrillPress.SampleRules", "samples");
        var environment = new Dictionary<string, string>
        {
            ["TMPDIR"] = temporaryRoot.FullName,
            ["TMP"] = temporaryRoot.FullName,
            ["TEMP"] = temporaryRoot.FullName,
        };
        var cancellationToken = Xunit.TestContext.Current.CancellationToken;
        var result = await RunProcessAsync(
            "dotnet",
            [cli, "check", "--build-host", buildHost, "--rules", rules, target, .. options],
            RepositoryRoot,
            cancellationToken,
            environment);

        return new CliResult(
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            temporaryRoot.FullName);
    }

    protected sealed record CliResult(
        int ExitCode,
        string StandardOutput,
        string StandardError,
        string TemporaryRoot);

    protected sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
