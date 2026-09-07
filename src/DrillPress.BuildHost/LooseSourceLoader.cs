using System.IO.Abstractions;
using DrillPress.Engine;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress.BuildHost;

internal sealed class LooseSourceLoader(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    public Task<SnapshotExport> LoadAsync(string[] paths, SnapshotLoadOptions options, CancellationToken cancellationToken)
    {
        if (options.Properties.Count > 0)
        {
            throw new ArgumentException("MSBuild properties apply only to solution and project targets.");
        }

        var trees = paths.Select(path =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = _fileSystem.File.OpenRead(path);
            return CSharpSyntaxTree.ParseText(SourceText.From(stream, System.Text.Encoding.UTF8),
                new CSharpParseOptions(LanguageVersion.CSharp14), path, cancellationToken: cancellationToken);
        }).ToArray();
        var referencePaths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(_fileSystem.Path.PathSeparator)
            ?? throw new InvalidOperationException("The BuildHost runtime did not provide its platform assemblies.");
        var references = referencePaths.Distinct().Order(StringComparer.Ordinal).Select(path =>
        {
            using var stream = _fileSystem.File.OpenRead(path);
            return MetadataReference.CreateFromStream(stream, filePath: path);
        }).ToArray();
        var compilation = CSharpCompilation.Create("DrillPress.LooseSource", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        CompilationValidation.Validate(compilation, options.ValidateCompilation, cancellationToken);
        var snapshot = new CompilerCapture(_fileSystem).Capture("LooseSource", paths[0], "loose", compilation, [], [], cancellationToken);
        return Task.FromResult(new SnapshotExport(CompilationSnapshot.Create(snapshot), [new CompilationContext(snapshot, compilation)]));
    }
}
