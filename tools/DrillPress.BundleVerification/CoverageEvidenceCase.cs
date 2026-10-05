using System.IO.Abstractions;
using System.Text;
using System.Xml.Linq;
using DrillPress.Manifest;

namespace DrillPress.BundleVerification;

internal sealed class CoverageEvidenceCase(
    IFileSystem fileSystem,
    string repository,
    string fixture,
    string output,
    string rid
)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    internal async Task VerifyAsync()
    {
        var directory = _fileSystem.Directory.CreateDirectory(
            _fileSystem.Path.Combine(output, "coverage-evidence")
        );
        var projectPath = await WriteProjectAsync(directory.FullName);
        var snapshotPath = _fileSystem.Path.Combine(fixture, "violating.json");
        var expected = await ExpectedOutputAsync(snapshotPath);
        foreach (var mode in Enum.GetValues<BundleMode>())
            await VerifyModeAsync(directory.FullName, projectPath, snapshotPath, expected, mode);
        Console.WriteLine(
            "Coverage evidence: managed/native typed reasons and source context match"
        );
    }

    private async Task<string> WriteProjectAsync(string directory)
    {
        var projectPath = _fileSystem.Path.Combine(directory, "CoverageProbe.csproj");
        var project = new XElement(
            "Project",
            new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement(
                "PropertyGroup",
                new XElement("OutputType", "Exe"),
                new XElement("TargetFramework", "net10.0"),
                new XElement("ImplicitUsings", "enable"),
                new XElement("PublishAot", "true"),
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
        await _fileSystem.File.WriteAllTextAsync(
            _fileSystem.Path.Combine(directory, "Program.cs"),
            """
            using DrillPress;
            using DrillPress.Engine;
            var rules = new RuleSet();
            rules.For(CodeType.Of<string>().Member("Empty").References)
                .Require(Coverage.Executed, "COV", "Exercise source occurrence.");
            return (int)await new RuleApplication().RunAsync(rules, args);
            """
        );
        return projectPath;
    }

    private async Task<byte[]> ExpectedOutputAsync(string snapshotPath)
    {
        var snapshot = await new CompilationSnapshotFile(_fileSystem).ReadAsync(snapshotPath);
        var context = snapshot.Projects.Single();
        var document = context.Documents.Single(document =>
            document.Path.EndsWith("Probe.cs", StringComparison.Ordinal)
        );
        var finding = new Finding(
            "COV",
            "Exercise source occurrence.",
            document.DocumentId,
            document.Text.IndexOf("Text.Empty"),
            "Text.Empty".Length,
            null
        )
        {
            Evidence = "coverage: unknown (no-tests)",
            Coverage =
            [
                new(
                    ExecutionCoverage.Unknown,
                    [CoverageReason.NoApplicableTests],
                    context.Name,
                    context.TargetFramework,
                    context.ContextId
                ),
            ],
        };
        return BundleResponseProtocol.Serialize(
            new(
                BundleResponseProtocol.CurrentVersion,
                snapshot.RequestId,
                [new(context.ContextId, true, [finding])],
                []
            )
        );
    }

    private async Task VerifyModeAsync(
        string directory,
        string projectPath,
        string snapshotPath,
        byte[] expected,
        BundleMode mode
    )
    {
        var destination = _fileSystem.Path.Combine(directory, mode.ToString());
        var arguments = new List<string>
        {
            "publish",
            projectPath,
            "-c",
            "Release",
            "-o",
            destination,
            "-p:PublishAot=" + (mode == BundleMode.Native ? "true" : "false"),
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
            _fileSystem.Path.Combine(output, $"coverage-publish.{mode}.log"),
            [.. published.StandardOutput, .. published.StandardError]
        );
        ProcessRunner.RequireSuccess(published);
        PublishWarnings.Validate(
            Encoding.UTF8.GetString(published.StandardOutput)
                + Encoding.UTF8.GetString(published.StandardError)
        );
        var executable = _fileSystem.Path.Combine(
            destination,
            mode == BundleMode.Managed ? "CoverageProbe.dll"
                : OperatingSystem.IsWindows() ? "CoverageProbe.exe"
                : "CoverageProbe"
        );
        var result = await ProcessRunner.RunAsync(
            mode == BundleMode.Managed ? "dotnet" : executable,
            mode == BundleMode.Managed
                ? [executable, "check", snapshotPath, "--explain-coverage"]
                : ["check", snapshotPath, "--explain-coverage"],
            repository
        );
        BundleContract.Validate(
            new("coverage-evidence", [], BundleOutcome.Findings, expected, []),
            result
        );
        await _fileSystem.File.WriteAllBytesAsync(
            _fileSystem.Path.Combine(output, $"coverage-evidence.{mode}.stdout"),
            result.StandardOutput
        );
    }
}
