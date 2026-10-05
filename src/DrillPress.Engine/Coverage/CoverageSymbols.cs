using System.IO.Abstractions;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DrillPress.Engine;

internal sealed class CoverageSymbols(IFileSystem fileSystem, CoverageProcess process)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly CoverageProcess _process = process;

    internal async Task<CoverageSymbolEvidence?> IdentityAsync(
        AnalysisProject project,
        string root,
        CancellationToken cancellationToken
    )
    {
        var output = await _process.RunAsync(
            "dotnet",
            [
                "msbuild",
                project.ProjectPath,
                "-getProperty:TargetPath",
                .. CoverageDiscovery.Properties(project),
                "-p:TargetFramework=" + project.TargetFramework,
            ],
            _fileSystem.Path.GetDirectoryName(project.ProjectPath)!,
            cancellationToken
        );
        var assembly = _fileSystem.Path.GetFullPath(output.Trim(), root);
        project.Coverage.Unavailable(CoverageReason.MissingSymbols);
        if (!_fileSystem.File.Exists(assembly))
            return null;
        using var stream = _fileSystem.File.OpenRead(assembly);
        try
        {
            using var pe = new PEReader(stream);
            if (
                !pe.TryOpenAssociatedPortablePdb(
                    assembly,
                    path => _fileSystem.File.Exists(path) ? _fileSystem.File.OpenRead(path) : null,
                    out var provider,
                    out _
                )
            )
                return null;
            using var symbols = provider!;
            var reader = symbols.GetMetadataReader();
            if (!CoverageCompilation.Matches(project, reader))
            {
                project.Coverage.Unavailable(CoverageReason.SymbolIdentityMismatch);
                return null;
            }
            new CoverageAdvancementSymbols(_fileSystem).Apply(project, pe, reader);
            var calls = new CoverageCallSymbols(_fileSystem).Read(project, pe, reader);
            return new(
                Convert.ToHexString(reader.DebugMetadataHeader!.Id.AsSpan()),
                calls.Proofs,
                calls.Occurrences
            );
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }
}
