using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using DrillPress.Manifest;

namespace DrillPress.BundleVerification;

internal sealed class RuleCoverageCase(IFileSystem fileSystem, string root, string directory)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    private static readonly string _source = """
        using System;
        using System.Linq;
        using Xunit;
        public interface IContract { }
        public sealed class Probe : IContract
        {
            [Fact] public void Test()
            {
                var values = new[] { string.Empty };

                Assert.NotNull(values);

                _ = values.Distinct(StringComparer.Ordinal);

                Assert.Single(values);
            }
        }
        """.ReplaceLineEndings("\n");

    public async Task<BundleCase> CreateAsync(string buildHost)
    {
        var projectPath = await WriteProjectAsync();
        var snapshotPath = _fileSystem.Path.Combine(directory, "all-rules.json");
        ProcessRunner.RequireSuccess(await ProcessRunner.RunAsync("dotnet", ["restore", projectPath, "--nologo"], root));
        ProcessRunner.RequireSuccess(await ProcessRunner.RunAsync("dotnet", [buildHost, "export", projectPath, snapshotPath, "--validate-compilation"], root));
        var snapshot = await new CompilationSnapshotFile(_fileSystem).ReadAsync(snapshotPath);
        var response = ExpectedResponse(snapshot);
        return new BundleCase("all-rules", ["check", snapshotPath], BundleOutcome.Findings, BundleResponseProtocol.Serialize(response), []);
    }

    private async Task<string> WriteProjectAsync()
    {
        _fileSystem.Directory.CreateDirectory(directory);
        var projectPath = _fileSystem.Path.Combine(directory, "Coverage.csproj");
        var referenceDirectory = _fileSystem.Path.Combine(root, "tests", "DrillPress.IntegrationTests", "bin", "Release", "net10.0");
        var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("IsTestProject", "false")),
            new XElement("ItemGroup", new[] { "xunit.v3.core", "xunit.v3.assert" }.Select(name =>
                new XElement("Reference", new XAttribute("Include", name),
                    new XElement("HintPath", _fileSystem.Path.Combine(referenceDirectory, name + ".dll"))))));
        await _fileSystem.File.WriteAllTextAsync(projectPath, project.ToString());
        await _fileSystem.File.WriteAllTextAsync(_fileSystem.Path.Combine(directory, "Coverage.cs"), _source);
        return projectPath;
    }

    private BundleResponse ExpectedResponse(CompilationSnapshot snapshot)
    {
        var project = snapshot.Projects.Single();
        var document = project.Documents.Single(document => document.Path == _fileSystem.Path.Combine(directory, "Coverage.cs"));
        var literal = ExpectedFix(document, "string.Empty", "\"\"", project.ContextId);
        var comparer = ExpectedFix(document, "(StringComparer.Ordinal)", "()", project.ContextId);
        Finding[] findings =
        [
            new("DP1001", "Keep at most two empty lines in a test.", document.DocumentId,
                _source.IndexOf("\n\n        Assert.Single", StringComparison.Ordinal) + 1, 0, null),
            Find("DP1002", "Move assertions after the final empty line.", "Assert.NotNull(values)", document),
            Find("DP1003", "Remove interfaces with exactly one concrete non-test implementation.", "IContract", document),
            Find("DP1004", "Use the empty string literal \"\" instead of string.Empty.", "string.Empty", document) with { BatchId = literal.Id },
            Find("DP1005", "Avoid passing StringComparer.Ordinal.", "StringComparer.Ordinal", document) with { BatchId = comparer.Id },
        ];
        return new(BundleResponseProtocol.CurrentVersion, snapshot.RequestId, [new(project.ContextId, true, findings)], [literal, comparer]);
    }

    private static Finding Find(string id, string message, string text, DocumentSnapshot document) =>
        new(id, message, document.DocumentId, _source.IndexOf(text, StringComparison.Ordinal), text.Length, null);

    private static FixBatch ExpectedFix(DocumentSnapshot document, string original, string replacement, string contextId)
    {
        var edit = new SourceEdit(document.FileIdentity, document.Fingerprint, _source.IndexOf(original, StringComparison.Ordinal), original.Length, original, replacement);
        var signature = $"{edit.FileIdentity.Length}:{edit.FileIdentity}{edit.Fingerprint}:{edit.Start}:{edit.Length}:{edit.Replacement.Length}:{edit.Replacement}";
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)));
        return new(id, [edit], [new(contextId, true)]);
    }
}
