using System.Collections.Concurrent;
using System.IO.Abstractions;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DrillPress.Engine;

internal sealed class GeneratorOptions(GeneratorAnalyzerConfig[] files, IFileSystem fileSystem)
    : AnalyzerConfigOptionsProvider
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly AnalyzerConfigSet _configurations = AnalyzerConfigSet.Create(
        files.Select(file => AnalyzerConfig.Parse(file.Text, file.Path)).ToArray()
    );
    private readonly ConcurrentDictionary<string, AnalyzerConfigOptionsResult> _results = new();

    public override AnalyzerConfigOptions GlobalOptions =>
        new GeneratorDictionaryOptions(_configurations.GlobalConfigOptions.AnalyzerOptions);

    internal AnalyzerConfigOptionsResult For(string path) =>
        path.Length == 0
            ? _configurations.GlobalConfigOptions
            : _results.GetOrAdd(
                _fileSystem.Path.GetFullPath(path),
                _configurations.GetOptionsForSourcePath
            );

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) =>
        new GeneratorDictionaryOptions(For(tree.FilePath).AnalyzerOptions);

    public override AnalyzerConfigOptions GetOptions(AdditionalText file) =>
        new GeneratorDictionaryOptions(For(file.Path).AnalyzerOptions);

    internal bool GlobalDiagnostic(string id, out ReportDiagnostic value) =>
        _configurations.GlobalConfigOptions.TreeOptions.TryGetValue(id, out value);
}
