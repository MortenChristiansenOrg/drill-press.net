using System.IO.Abstractions;

namespace DrillPress.BundleVerification;

public sealed class TargetCoverageCase(
    IFileSystem fileSystem,
    string repository,
    string fixture,
    string output
)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly string _repository = repository;
    private readonly string _fixture = fixture;
    private readonly string _output = output;

    public async Task VerifyAsync(
        string cli,
        string buildHost,
        string managed,
        string native,
        byte[] expected
    )
    {
        var project = _fileSystem.Path.Combine(_fixture, "Probe.csproj");
        var solution = _fileSystem.Path.Combine(_fixture, "Selected.slnx");
        await _fileSystem.File.WriteAllTextAsync(
            solution,
            "<Solution><Project Path=\"Probe.csproj\" /></Solution>"
        );
        var legacyDirectory = _fileSystem.Directory.CreateDirectory(
            _fileSystem.Path.Combine(_fixture, "Legacy")
        );
        var legacy = _fileSystem.Path.Combine(legacyDirectory.FullName, "Selected.sln");
        await _fileSystem.File.WriteAllTextAsync(
            legacy,
            $$$"""
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Probe", "{{{project}}}", "{77F24E93-8B73-46CC-BD2E-7947806D6792}"
            EndProject
            Global
            EndGlobal
            """
        );

        foreach (
            var (name, target) in new[]
            {
                ("sln", legacy),
                ("slnx", solution),
                ("project", project),
                ("directory", _fixture),
                ("file", _fileSystem.Path.Combine(_fixture, "Probe.cs")),
                ("glob", _fileSystem.Path.Combine(_fixture, "*.cs")),
            }
        )
        {
            foreach (
                var (mode, bundle) in new[]
                {
                    (BundleMode.Managed, managed),
                    (BundleMode.Native, native),
                }
            )
            {
                var result = await ProcessRunner.RunAsync(
                    "dotnet",
                    [cli, "check", "--build-host", buildHost, "--rules", bundle, target],
                    _repository
                );
                var prefix = _fileSystem.Path.Combine(_output, $"target-{name}.{mode}");
                await _fileSystem.File.WriteAllBytesAsync(
                    prefix + ".stdout",
                    result.StandardOutput
                );
                await _fileSystem.File.WriteAllBytesAsync(prefix + ".stderr", result.StandardError);
                BundleContract.Validate(
                    new(name, [], BundleOutcome.Findings, expected, []),
                    result
                );
            }

            Console.WriteLine($"CLI {name}: managed/native bytes and exit code match");
        }
    }
}
