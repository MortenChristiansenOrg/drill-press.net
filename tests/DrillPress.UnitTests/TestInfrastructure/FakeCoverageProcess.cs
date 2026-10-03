using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DrillPress.Engine;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class FakeCoverageProcess(
    MockFileSystem fileSystem,
    ProjectSnapshot project,
    string symbolIdentity
) : CoverageProcess
{
    private readonly MockFileSystem _fileSystem = fileSystem;
    private readonly ProjectSnapshot _project = project;
    private readonly string _symbolIdentity = symbolIdentity;
    public int Collections { get; private set; }
    public int Evaluations { get; private set; }
    public string[]? ReferencingTargets { get; set; }
    public bool Fail { get; set; }
    public bool HasTests { get; set; } = true;
    public bool Unrestored { get; set; }
    public bool TestProjectOptOut { get; set; }
    public string[] ImportedInputs { get; set; } = [];
    public bool Stale { get; set; }
    public bool DifferentBuild { get; set; }
    public bool Excluded { get; set; }
    public bool Ambiguous { get; set; }
    public bool MixedSequencePoints { get; set; }
    public bool LineOnly { get; set; }
    public bool MissingFunctionIdentity { get; set; }
    public bool OtherFrameworkModule { get; set; }
    public string State { get; set; } = "yes";
    public List<string[]> StatesByRun { get; } = [];
    public Action? DuringCollection { get; set; }
    public string[]? RestoredInputs { get; set; }
    public string? ExternalProject { get; set; }
    public string? ExternalSource { get; set; }

    internal override Task<string> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string directory,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (arguments[0] == "restore")
        {
            ImportedInputs = RestoredInputs ?? ImportedInputs;
            return Task.FromResult("");
        }
        if (arguments[0] == "merge")
        {
            Merge(arguments);
            return Task.FromResult("");
        }
        if (arguments[0] == "tool")
        {
            var index = arguments.ToList().IndexOf("--tool-path");
            _fileSystem.AddFile(
                _fileSystem.Path.Combine(
                    arguments[index + 1],
                    OperatingSystem.IsWindows() ? "dotnet-coverage.exe" : "dotnet-coverage"
                ),
                new MockFileData("tool")
            );
            return Task.FromResult("");
        }
        if (arguments[0] == "msbuild")
        {
            if (arguments.Contains("-getProperty:TargetPath"))
                return Task.FromResult(
                    _fileSystem.Path.ChangeExtension(_project.ProjectPath, ".dll")
                );
            var test =
                HasTests
                && _fileSystem.Path.GetFileNameWithoutExtension(arguments[1]).EndsWith("Tests");
            Evaluations++;
            return Task.FromResult(
                JsonSerializer.Serialize(
                    new
                    {
                        Properties = new
                        {
                            IsTestProject = TestProjectOptOut ? "false"
                            : test && Unrestored ? ""
                            : test ? "true"
                            : "false",
                            MSBuildAllProjects = string.Join(";", ImportedInputs),
                            TargetFramework = "net10.0",
                            TargetFrameworks = "",
                        },
                        Items = new
                        {
                            PackageReference = test ? new[] { new { Identity = "xunit.v3" } } : [],
                            ProjectReference = test
                                ? (ReferencingTargets ?? [_project.ProjectPath])
                                    .Select(path => new { FullPath = path })
                                    .ToArray()
                            : arguments[1] == _project.ProjectPath
                            && ExternalProject is { } externalProject
                                ? new[] { new { FullPath = externalProject } }
                            : [],
                            Compile = arguments[1] == ExternalProject
                            && ExternalSource is { } externalSource
                                ? new[] { new { FullPath = externalSource } }
                                : [],
                        },
                    }
                )
            );
        }
        Collections++;
        if (Fail)
            throw new InvalidOperationException("Coverage collection failed: tests failed.");
        var outputIndex = arguments.ToList().IndexOf("-o");
        var path = arguments[outputIndex + 1];
        var source = _project.Documents[0];
        var text = SourceText.From(source.Text);
        var syntax = CSharpSyntaxTree.ParseText(source.Text).GetRoot();
        var ranges = syntax
            .DescendantNodes()
            .OfType<StatementSyntax>()
            .Where(node =>
                node
                    is ExpressionStatementSyntax
                        or LocalDeclarationStatementSyntax
                        or ReturnStatementSyntax
                        or ThrowStatementSyntax
            )
            .Select(
                (node, index) =>
                {
                    var position = text.Lines.GetLinePositionSpan(node.Span);
                    return new XElement(
                        "range",
                        new XAttribute("source_id", "0"),
                        new XAttribute(
                            "covered",
                            StatesByRun
                                .ElementAtOrDefault(Collections - 1)
                                ?.ElementAtOrDefault(index)
                                ?? State
                        ),
                        new XAttribute("start_line", position.Start.Line + 1),
                        new XAttribute("end_line", position.End.Line + 1),
                        new XAttribute("start_column", LineOnly ? 0 : position.Start.Character + 1),
                        new XAttribute("end_column", position.End.Character + 1)
                    );
                }
            );
        var module = new XElement(
            "module",
            new XAttribute("name", _project.AssemblyName + ".dll"),
            new XAttribute("id", DifferentBuild ? "other-build" : _symbolIdentity),
            new XElement(
                "source_files",
                new XElement(
                    "source_file",
                    new XAttribute("id", "0"),
                    new XAttribute("path", source.Path),
                    new XAttribute("checksum_type", "SHA256"),
                    new XAttribute(
                        "checksum",
                        Stale
                            ? "stale"
                            : Convert.ToHexString(
                                SHA256.HashData(Encoding.UTF8.GetBytes(source.Text))
                            )
                    )
                )
            ),
            new XElement(
                "functions",
                new XElement(
                    "function",
                    MissingFunctionIdentity ? null : new XAttribute("token", "0x6000001"),
                    new XElement("ranges", Excluded ? [] : ranges)
                )
            )
        );
        if (MixedSequencePoints)
        {
            foreach (var range in module.Descendants("range").ToArray())
            {
                var other = new XElement(range);
                other.SetAttributeValue("covered", "no");
                range.Parent!.Add(other);
            }
        }
        if (Ambiguous)
        {
            var function = module.Descendants("function").Single();
            function.SetAttributeValue("token", "0x6000001");
            var other = new XElement(function);
            other.SetAttributeValue("token", "0x6000002");
            foreach (var range in other.Descendants("range"))
                range.SetAttributeValue("covered", "no");
            module.Element("functions")!.Add(other);
        }
        var modules = new XElement("modules", module);
        if (OtherFrameworkModule)
        {
            var other = new XElement(module);
            other.SetAttributeValue("id", "other-framework");
            foreach (var range in other.Descendants("range"))
                range.SetAttributeValue("covered", "no");
            modules.Add(other);
        }
        _fileSystem.AddFile(
            path,
            new MockFileData(new XDocument(new XElement("results", modules)).ToString())
        );
        DuringCollection?.Invoke();
        return Task.FromResult("");
    }

    private void Merge(IReadOnlyList<string> arguments)
    {
        var outputIndex = arguments.ToList().IndexOf("-o");
        var runs = arguments
            .Skip(outputIndex + 2)
            .Select(path =>
            {
                using var stream = _fileSystem.File.OpenRead(path);
                return XDocument.Load(stream).Root!;
            })
            .ToArray();
        var modules = runs.SelectMany(
                (run, index) =>
                    run.Descendants("module").Select(module => (Module: module, Run: index))
            )
            .GroupBy(item =>
                ((string?)item.Module.Attribute("id"), (string?)item.Module.Attribute("name"))
            )
            .Select(group =>
            {
                var module = new XElement(group.First().Module);
                var functions = group
                    .SelectMany(item =>
                        item.Module.Descendants("function")
                            .Select(function => (Function: function, item.Run))
                    )
                    .GroupBy(item => (string?)item.Function.Attribute("token") ?? "")
                    .Select(functionGroup =>
                    {
                        var function = new XElement(functionGroup.First().Function);
                        var ranges = functionGroup
                            .SelectMany(item =>
                                item.Function.Descendants("range")
                                    .Select(range => (Range: range, item.Run))
                            )
                            .GroupBy(item =>
                                string.Join(
                                    "|",
                                    item.Range.Attributes()
                                        .Where(attribute => attribute.Name != "covered")
                                        .Select(attribute => attribute.Value)
                                )
                            )
                            .Select(rangeGroup =>
                            {
                                var range = new XElement(rangeGroup.First().Range);
                                var states = rangeGroup
                                    .GroupBy(item => item.Run)
                                    .Select(run =>
                                        run.All(item =>
                                            (string?)item.Range.Attribute("covered") == "yes"
                                        )
                                            ? "yes"
                                        : run.All(item =>
                                            (string?)item.Range.Attribute("covered") == "no"
                                        )
                                            ? "no"
                                        : "partial"
                                    )
                                    .ToArray();
                                range.SetAttributeValue(
                                    "covered",
                                    states.Contains("yes") ? "yes"
                                        : states.All(state => state == "no") ? "no"
                                        : "partial"
                                );
                                return range;
                            });
                        function.ReplaceNodes(new XElement("ranges", ranges));
                        return function;
                    });
                module.Element("functions")!.ReplaceNodes(functions);
                return module;
            });
        _fileSystem.AddFile(
            arguments[outputIndex + 1],
            new MockFileData(
                new XDocument(new XElement("results", new XElement("modules", modules))).ToString()
            )
        );
    }
}
