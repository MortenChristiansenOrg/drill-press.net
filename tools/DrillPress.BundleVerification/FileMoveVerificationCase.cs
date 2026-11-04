using System.IO.Abstractions;
using System.Text;

namespace DrillPress.BundleVerification;

// A misnamed file in an SDK project, renamed after its type by the sample rule with each bundle mode.
internal sealed class FileMoveVerificationCase(
    IFileSystem fileSystem,
    string root,
    string directory,
    string output
)
{
    private const string Source =
        "namespace Orders;\n\npublic class Invoice { public string Number => string.Empty; }\n";

    private readonly IFileSystem _fileSystem = fileSystem;

    public async Task VerifyAsync(
        string cli,
        string buildHost,
        string managedBundle,
        string nativeBundle
    )
    {
        _fileSystem.Directory.CreateDirectory(directory);
        var project = _fileSystem.Path.Combine(directory, "Orders.csproj");
        var original = _fileSystem.Path.Combine(directory, "Misnamed.cs");
        var renamed = _fileSystem.Path.Combine(directory, "Invoice.cs");
        await _fileSystem.File.WriteAllTextAsync(
            project,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup></Project>"
        );
        var restore = await ProcessRunner.RunAsync(
            "dotnet",
            ["restore", project, "--nologo"],
            root
        );
        if (restore.ExitCode != 0)
            throw new InvalidOperationException("The file move fixture could not be restored.");
        var expected = Encoding.UTF8.GetBytes(Source.Replace("string.Empty", "\"\""));
        foreach (var mode in Enum.GetValues<BundleMode>())
        {
            if (_fileSystem.File.Exists(renamed))
                _fileSystem.File.Delete(renamed);
            await _fileSystem.File.WriteAllTextAsync(original, Source, new UTF8Encoding(false));
            var result = await ProcessRunner.RunAsync(
                "dotnet",
                [
                    cli,
                    "fix",
                    "--build-host",
                    buildHost,
                    "--rules",
                    mode == BundleMode.Managed ? managedBundle : nativeBundle,
                    project,
                ],
                root
            );
            await _fileSystem.File.WriteAllBytesAsync(
                _fileSystem.Path.Combine(output, $"move.{mode}.stdout"),
                result.StandardOutput
            );
            await _fileSystem.File.WriteAllBytesAsync(
                _fileSystem.Path.Combine(output, $"move.{mode}.stderr"),
                result.StandardError
            );
            if (
                result.ExitCode != 0
                || result.StandardOutput.Length != 0
                || result.StandardError.Length != 0
                || _fileSystem.File.Exists(original)
                || !_fileSystem.File.Exists(renamed)
                || !(await _fileSystem.File.ReadAllBytesAsync(renamed))
                    .AsSpan()
                    .SequenceEqual(expected)
            )
            {
                throw new InvalidDataException(
                    $"{mode} fix/recheck did not rename the file with its edit and a clean recheck."
                );
            }
        }
    }
}
