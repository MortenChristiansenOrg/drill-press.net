using System.Diagnostics;

namespace DrillPress.IntegrationTests;

public abstract class IntegrationTest : IDisposable
{
    private readonly List<DirectoryInfo> _temporaryDirectories = [];

    protected static string RepositoryRoot { get; } = FindRepositoryRoot();

    protected static string SampleProjectPath { get; } = RepositoryPath(
        "Sample Solution",
        "src",
        "WidgetLibrary",
        "WidgetLibrary.csproj");

    public void Dispose()
    {
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

    protected static string GetOutputPath(string projectName) =>
        RepositoryPath("src", projectName, "bin", "Debug", "net10.0", $"{projectName}.dll");

    protected static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
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
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
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
