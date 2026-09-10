using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using DrillPress.BundleVerification;

namespace DrillPress.Conformance;

public sealed class PinnedXunit(IFileSystem fileSystem)
{
    // Includes SourceLink 10.0.112, avoiding the vulnerable Microsoft.Build.Tasks.Git 10.0.105 dependency.
    public const string Revision = "e10d47b2a123f880c209d07b5c4e0d44810ab4ac";
    private readonly IFileSystem _fileSystem = fileSystem;

    public async Task<string> PrepareAsync(string directory, string reportPath, CancellationToken cancellationToken)
    {
        directory = _fileSystem.Path.GetFullPath(directory);
        reportPath = _fileSystem.Path.GetFullPath(reportPath);
        _fileSystem.Directory.CreateDirectory(_fileSystem.Path.GetDirectoryName(reportPath)!);
        var operations = new List<object>();
        try
        {
            if (!_fileSystem.Directory.Exists(directory))
            {
                var parent = _fileSystem.Path.GetDirectoryName(directory)!;
                _fileSystem.Directory.CreateDirectory(parent);
                await Run("git", ["clone", "https://github.com/xunit/xunit.git", directory], parent);
                await Run("git", ["checkout", "--detach", Revision], directory);
            }

            var head = await Run("git", ["rev-parse", "HEAD"], directory);
            if (head.Trim() != Revision)
            {
                throw new InvalidOperationException($"Expected xUnit revision {Revision}; use a new checkout directory.");
            }

            var status = await Run("git", ["status", "--porcelain", "--untracked-files=no"], directory);
            if (status.Length > 0)
            {
                throw new InvalidOperationException("The pinned xUnit checkout has tracked modifications; use a clean checkout.");
            }

            await Run("git", ["submodule", "update", "--init", "--recursive"], directory);
            await Run("git", ["submodule", "status", "--recursive"], directory);
            await Run("dotnet", ["--info"], directory);
            await Run("dotnet", ["restore", "xunit.slnx", "--use-lock-file", "--lock-file-path", "obj/drillpress.packages.lock.json", "-p:RestoreLockedMode=false", "--nologo"], directory);
            var lockDirectory = _fileSystem.Path.Combine(_fileSystem.Path.GetDirectoryName(reportPath)!, "dependency-locks");
            foreach (var path in _fileSystem.Directory.EnumerateFiles(directory, "drillpress.packages.lock.json", SearchOption.AllDirectories))
            {
                var copy = _fileSystem.Path.Combine(lockDirectory, _fileSystem.Path.GetRelativePath(directory, path));
                _fileSystem.Directory.CreateDirectory(_fileSystem.Path.GetDirectoryName(copy)!);
                _fileSystem.File.Copy(path, copy, true);
            }
            return _fileSystem.Path.Combine(directory, "xunit.slnx");
        }
        finally
        {
            await _fileSystem.File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { revision = Revision, directory, operations },
                new JsonSerializerOptions { WriteIndented = true }), CancellationToken.None);
        }

        async Task<string> Run(string executable, string[] arguments, string workingDirectory)
        {
            var result = await ProcessRunner.RunAsync(executable, arguments, workingDirectory, cancellationToken, TimeSpan.FromMinutes(20));
            var stdout = Encoding.UTF8.GetString(result.StandardOutput);
            var stderr = Encoding.UTF8.GetString(result.StandardError);
            operations.Add(new { executable, arguments, workingDirectory, result.ExitCode, stdout, stderr });
            ProcessRunner.RequireSuccess(result);
            return stdout;
        }
    }
}
