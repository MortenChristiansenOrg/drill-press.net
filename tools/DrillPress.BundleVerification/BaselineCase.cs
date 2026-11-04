using System.IO.Abstractions;
using System.Text;

namespace DrillPress.BundleVerification;

/// <summary>Checks that managed and native bundles build the same accepted baseline from a directory.</summary>
internal sealed class BaselineCase(
    IFileSystem fileSystem,
    string repository,
    string directory,
    string output
)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    public async Task VerifyAsync(string cli, string buildHost, string managed, string native)
    {
        var current = _fileSystem.Path.Combine(directory, "current");
        var accepted = _fileSystem.Path.Combine(directory, "accepted");
        var project = _fileSystem.Path.Combine("CodecExamples", "CodecExamples.csproj");
        await WriteAsync(
            current,
            project,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <LangVersion>14.0</LangVersion>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """
        );
        await WriteAsync(
            current,
            "CodecExamples/Existing.Legacy.cs",
            "namespace CodecExamples;\npublic sealed class Existing { public int Value; }\n"
        );
        await WriteAsync(
            current,
            "CodecExamples/Added.Legacy.cs",
            "namespace CodecExamples;\npublic sealed class Added { }\n"
        );
        await WriteAsync(
            accepted,
            "CodecExamples/Existing.Legacy.cs",
            "namespace CodecExamples;\npublic sealed class Existing { }\n"
        );
        ProcessRunner.RequireSuccess(
            await ProcessRunner.RunAsync(
                "dotnet",
                ["restore", _fileSystem.Path.Combine(current, project), "--nologo"],
                repository
            )
        );
        var expected = Encoding.UTF8.GetBytes(
            "SDK2013 Add new codec implementations to the current format layer.\nCodecExamples/Added.Legacy.cs\n  1\n"
        );
        foreach (
            var (name, options) in new[]
            {
                ("baseline", Array.Empty<string>()),
                ("baseline-stream", ["--stream-compilation"]),
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
                    [
                        cli,
                        "check",
                        "--build-host",
                        buildHost,
                        "--rules",
                        bundle,
                        project,
                        "--baseline",
                        "../accepted",
                        .. options,
                    ],
                    current
                );
                var prefix = _fileSystem.Path.Combine(output, $"{name}.{mode}");
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

            Console.WriteLine($"CLI {name}: managed/native accepted baselines match");
        }
    }

    private async Task WriteAsync(string root, string path, string text)
    {
        var file = _fileSystem.Path.Combine(root, path);
        _fileSystem.Directory.CreateDirectory(_fileSystem.Path.GetDirectoryName(file)!);
        await _fileSystem.File.WriteAllTextAsync(file, text.ReplaceLineEndings("\n"));
    }
}
