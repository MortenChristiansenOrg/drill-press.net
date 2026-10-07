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
                            "src/DrillPress.Testing/DrillPress.Testing.csproj"
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
            using DrillPress.Testing;
            if (args is ["fixture-policy"])
            {
                var workspace = new RuleTestWorkspace([]);
                workspace.AddProject("Policy", [new("Policy.cs",
                    "namespace System { public class Object {} public class ValueType {} public struct Void {} } class C { static void Hit() {} void M() { Hit(); } }")]);
                workspace.WithCoverage(facts => facts.ForCall("Policy.cs", "Hit()")
                    .Unknown(CoverageReason.UnsupportedExpressionMapping));
                var policyRules = new RuleSet();
                policyRules.Rule("POL", "Verify execution.")
                    .For(Code.Calls.Where(call => call.Target.Name == "Hit"))
                    .Require(Coverage.Executed.ReviewUnknownFor(CoverageReason.UnsupportedExpressionMapping));
                var result = await workspace.CheckAsync(policyRules);
                var finding = result.Findings.Single();
                var evidence = finding.Coverage.Single();
                Console.Write($"{finding.Disposition} {evidence.State} {evidence.SatisfiesRequirement} {evidence.IsReviewEligible}");
                return 0;
            }
            var rules = new RuleSet();
            rules.Rule("COV", "Exercise source occurrence.")
                .For(CodeType.Of<string>().Member("Empty").References)
                .Require(Coverage.Executed);
            rules.Rule("POL", "Verify execution.")
                .For(CodeType.Of<string>().Member("Empty").References)
                .Require(Coverage.Executed.ReviewUnknownFor(CoverageReason.UnsupportedExpressionMapping)
                    .OnUnknown("Inspect coverage evidence."));
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
                [
                    new(
                        context.ContextId,
                        true,
                        [
                            finding,
                            finding with
                            {
                                RuleId = "POL",
                                Message = "Verify execution.",
                                OutcomeRemediation = "Inspect coverage evidence.",
                                Coverage =
                                [
                                    finding.Coverage![0] with
                                    {
                                        ReviewReasons =
                                        [
                                            CoverageReason.UnsupportedExpressionMapping,
                                        ],
                                    },
                                ],
                            },
                        ]
                    ),
                ],
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
        var policy = await ProcessRunner.RunAsync(
            mode == BundleMode.Managed ? "dotnet" : executable,
            mode == BundleMode.Managed ? [executable, "fixture-policy"] : ["fixture-policy"],
            repository
        );
        BundleContract.Validate(
            new(
                "fixture-policy",
                [],
                BundleOutcome.Clean,
                Encoding.UTF8.GetBytes("Review Unknown False True"),
                []
            ),
            policy
        );
        await _fileSystem.File.WriteAllBytesAsync(
            _fileSystem.Path.Combine(output, $"coverage-policy.{mode}.stdout"),
            policy.StandardOutput
        );
    }
}
