using System.IO.Abstractions;
using System.Text;
using System.Xml.Linq;
using DrillPress.Manifest;

namespace DrillPress.BundleVerification;

internal sealed class EnumerationCoverageCase(
    IFileSystem fileSystem,
    string repository,
    string fixture,
    string output,
    string rid
)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    internal async Task VerifyAsync(string buildHost)
    {
        var directory = _fileSystem.Directory.CreateDirectory(
            _fileSystem.Path.Combine(fixture, "Enumeration")
        );
        var target = await WriteTargetAsync(directory.FullName);
        var snapshotPath = _fileSystem.Path.Combine(directory.FullName, "snapshot.json");
        ProcessRunner.RequireSuccess(
            await ProcessRunner.RunAsync("dotnet", ["restore", target], repository)
        );
        ProcessRunner.RequireSuccess(
            await ProcessRunner.RunAsync(
                "dotnet",
                [buildHost, "export", target, snapshotPath],
                repository
            )
        );
        var consumer = await WriteConsumerAsync(directory.FullName);
        byte[]? expected = null;
        foreach (var mode in Enum.GetValues<BundleMode>())
        {
            var result = await ExecuteAsync(consumer, snapshotPath, mode);
            var snapshot = await new CompilationSnapshotFile(_fileSystem).ReadAsync(snapshotPath);
            var validated = BundleResponseProtocol.Read(
                Encoding.UTF8.GetString(result.StandardOutput),
                snapshot
            );
            var evidence = validated.Findings.Select(finding => finding.Evidence).ToArray();
            string[] required =
            [
                "enumeration: uncovered",
                "enumeration: uncovered",
                "enumeration: unknown (unsupported-enumeration)",
            ];
            if (
                !evidence.SequenceEqual(required)
                || validated.Findings.Any(finding =>
                    finding.Coverage!.Single().Metric != CoverageMetric.Enumeration
                )
            )
                throw new InvalidDataException(
                    "Enumeration advancement did not retain its exact independent outcomes."
                );
            expected ??= result.StandardOutput;
            BundleContract.Validate(
                new("enumeration", [], BundleOutcome.Findings, expected, []),
                result
            );
            await _fileSystem.File.WriteAllBytesAsync(
                _fileSystem.Path.Combine(output, $"enumeration.{mode}.stdout"),
                result.StandardOutput
            );
        }
        Console.WriteLine(
            "Enumeration: managed/native empty, async, skipped, acquisition and indexed outcomes match"
        );
    }

    private async Task<string> WriteTargetAsync(string directory)
    {
        var target = _fileSystem.Directory.CreateDirectory(
            _fileSystem.Path.Combine(directory, "Target")
        );
        var tests = _fileSystem.Directory.CreateDirectory(
            _fileSystem.Path.Combine(directory, "Tests")
        );
        await WriteAsync(directory, "Workspace.slnx", "<Solution />");
        await WriteAsync(
            directory,
            "global.json",
            _fileSystem.File.ReadAllText(_fileSystem.Path.Combine(repository, "global.json"))
        );
        await WriteAsync(
            directory,
            "Directory.Packages.props",
            new XElement(
                "Project",
                new XElement(
                    "Import",
                    new XAttribute(
                        "Project",
                        _fileSystem.Path.Combine(repository, "Directory.Packages.props")
                    )
                )
            ).ToString()
        );
        await WriteAsync(
            target.FullName,
            "Target.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>
            """
        );
        await WriteAsync(
            target.FullName,
            "Loops.cs",
            """
            using System.Collections;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            public static class Loops {
                public static IEnumerable<int> Empty() => System.Array.Empty<int>();
                public static async IAsyncEnumerable<int> AsyncEmpty() { await Task.CompletedTask; yield break; }
                public static void Sync() { foreach (var item in Empty()) { } }
                public static async Task Async() { await foreach (var item in AsyncEmpty()) { } }
                public static void AcquisitionFails() { foreach (var item in (IEnumerable<int>)new Broken()) { } }
                public static void Never() { foreach (var item in Empty()) { } }
                public static void Indexed() { foreach (var item in System.Array.Empty<int>()) { } }
            }
            public sealed class Broken : IEnumerable<int> {
                public IEnumerator<int> GetEnumerator() => throw new System.InvalidOperationException();
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }
            """
        );
        await WriteAsync(
            tests.FullName,
            "Tests.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport><UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner></PropertyGroup>
              <ItemGroup><PackageReference Include="xunit.v3" /><ProjectReference Include="../Target/Target.csproj" /></ItemGroup>
            </Project>
            """
        );
        await WriteAsync(
            tests.FullName,
            "Tests.cs",
            """
            public class LoopTests {
                [Xunit.Fact] public async System.Threading.Tasks.Task EmptyLoops() {
                    Loops.Sync(); await Loops.Async(); Loops.Indexed();
                    Xunit.Assert.Throws<System.InvalidOperationException>(Loops.AcquisitionFails);
                }
            }
            """
        );
        return _fileSystem.Path.Combine(target.FullName, "Target.csproj");
    }

    private async Task<string> WriteConsumerAsync(string directory)
    {
        var consumer = _fileSystem.Directory.CreateDirectory(
            _fileSystem.Path.Combine(directory, "Consumer")
        );
        var projectPath = _fileSystem.Path.Combine(consumer.FullName, "EnumerationProbe.csproj");
        var project = new XElement(
            "Project",
            new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement(
                "PropertyGroup",
                new XElement("OutputType", "Exe"),
                new XElement("TargetFramework", "net10.0"),
                new XElement("TrimmerSingleWarn", "false"),
                new XElement("IlcTreatWarningsAsErrors", "true"),
                new XElement("WarningsNotAsErrors", "$(WarningsNotAsErrors);IL2091;IL3000")
            ),
            new XElement(
                "ItemGroup",
                new XElement(
                    "ProjectReference",
                    new XAttribute(
                        "Include",
                        _fileSystem.Path.Combine(
                            repository,
                            "src/DrillPress.Engine/DrillPress.Engine.csproj"
                        )
                    )
                )
            )
        );
        await _fileSystem.File.WriteAllTextAsync(projectPath, project.ToString());
        await WriteAsync(
            consumer.FullName,
            "Program.cs",
            """
            using DrillPress;
            using DrillPress.Engine;
            var rules = new RuleSet();
            rules.For(Code.Enumerations).Require(Coverage.EnumerationStarted, "ENUM", "Start enumeration.");
            return (int)await new RuleApplication().RunAsync(rules, args);
            """
        );
        return projectPath;
    }

    private async Task<ProcessOutput> ExecuteAsync(
        string project,
        string snapshotPath,
        BundleMode mode
    )
    {
        var destination = _fileSystem.Path.Combine(output, "enumeration", mode.ToString());
        var arguments = new List<string>
        {
            "publish",
            project,
            "-c",
            "Release",
            "-o",
            destination,
            "-p:PublishAot=" + (mode == BundleMode.Native ? "true" : "false"),
            "--self-contained",
            mode == BundleMode.Native ? "true" : "false",
        };
        if (mode == BundleMode.Native)
            arguments.AddRange(["-r", rid]);
        var published = await ProcessRunner.RunAsync(
            "dotnet",
            arguments.ToArray(),
            repository,
            timeout: TimeSpan.FromMinutes(10)
        );
        await _fileSystem.File.WriteAllBytesAsync(
            _fileSystem.Path.Combine(output, $"enumeration-publish.{mode}.log"),
            [.. published.StandardOutput, .. published.StandardError]
        );
        ProcessRunner.RequireSuccess(published);
        PublishWarnings.Validate(
            Encoding.UTF8.GetString(published.StandardOutput)
                + Encoding.UTF8.GetString(published.StandardError)
        );
        var executable = _fileSystem.Path.Combine(
            destination,
            mode == BundleMode.Managed ? "EnumerationProbe.dll"
                : OperatingSystem.IsWindows() ? "EnumerationProbe.exe"
                : "EnumerationProbe"
        );
        return await ProcessRunner.RunAsync(
            mode == BundleMode.Managed ? "dotnet" : executable,
            mode == BundleMode.Managed
                ? [executable, "check", snapshotPath, "--explain-coverage"]
                : ["check", snapshotPath, "--explain-coverage"],
            repository,
            timeout: TimeSpan.FromMinutes(10)
        );
    }

    private Task WriteAsync(string directory, string name, string text) =>
        _fileSystem.File.WriteAllTextAsync(_fileSystem.Path.Combine(directory, name), text);
}
