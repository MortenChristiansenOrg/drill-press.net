using System.Diagnostics;
using System.Reflection;

namespace DrillPress.IntegrationTests;

public abstract class IntegrationTest : IDisposable
{
    private readonly List<DirectoryInfo> _temporaryDirectories = [];
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

    protected DirectoryInfo CreateTemporaryDirectory(string prefix)
    {
        var directory = Directory.CreateTempSubdirectory(prefix);
        _temporaryDirectories.Add(directory);
        return directory;
    }

    protected static string RepositoryPath(params string[] segments) =>
        Path.Combine(RepositoryRoot, Path.Combine(segments));

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

    protected async Task<Process> WaitForTestProcessAsync(string readyPath)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            Xunit.TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (!File.Exists(readyPath))
        {
            await Task.Delay(20, timeout.Token);
        }

        var process = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(readyPath, timeout.Token)));
        _testProcesses.Add(process);
        return process;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DrillPress.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    protected sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
