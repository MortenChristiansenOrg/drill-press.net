using System.IO.Abstractions;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DrillPress.BuildHost;

/// <summary>
/// Applies the target build's compiler version to analyzer loading. The BuildHost's own Roslyn
/// may be newer, but a real build skips analyzers that reference a newer compiler than it runs.
/// </summary>
internal sealed class TargetCompiler(IFileSystem fileSystem)
{
    private const string CompilerAssemblyName = "Microsoft.CodeAnalysis";

    private readonly IFileSystem _fileSystem = fileSystem;

    /// <summary>Resolves the compiler assembly that the Csc task runs for an evaluated project.</summary>
    public string? CompilerAssemblyPath(
        string projectDirectory,
        string cscToolPath,
        string roslynTasksAssembly
    )
    {
        var directory =
            cscToolPath.Length > 0 ? cscToolPath
            : roslynTasksAssembly.Length > 0
                ? _fileSystem.Path.Combine(
                    _fileSystem.Path.GetDirectoryName(Normalize(roslynTasksAssembly))!,
                    "bincore"
                )
            : null;
        return directory is null
            ? null
            : _fileSystem.Path.GetFullPath(
                _fileSystem.Path.Combine(Normalize(directory), CompilerAssemblyName + ".dll"),
                projectDirectory
            );
    }

    /// <summary>Keeps analyzers the compiler can load and reports CS9057 for the rest; unknown compilers keep every analyzer.</summary>
    public ProjectAnalyzers Select(
        string projectName,
        IEnumerable<AnalyzerReference> references,
        string? compilerAssemblyPath
    )
    {
        var all = references.ToArray();
        var compiler =
            compilerAssemblyPath is not null && _fileSystem.File.Exists(compilerAssemblyPath)
                ? ReadMetadata(
                    compilerAssemblyPath,
                    reader => reader.IsAssembly ? reader.GetAssemblyDefinition().Version : null
                )
                : null;
        if (compiler is null)
            return new ProjectAnalyzers(projectName, all, []);

        var compatible = new List<AnalyzerReference>();
        var rejections = new List<Diagnostic>();
        foreach (var reference in all)
        {
            if (
                reference is AnalyzerFileReference file
                && _fileSystem.File.Exists(file.FullPath)
                && ReferencedCompiler(file.FullPath) is { } referenced
                && referenced > compiler
            )
                rejections.Add(
                    ProjectAnalyzers.CompilerWarning(
                        "CS9057",
                        $"Analyzer assembly '{file.FullPath}' cannot be used because it references version '{referenced}' of the compiler, which is newer than the currently running version '{compiler}'."
                    )
                );
            else
                compatible.Add(reference);
        }

        return new ProjectAnalyzers(projectName, compatible.ToArray(), rejections.ToArray());
    }

    private Version? ReferencedCompiler(string path) =>
        ReadMetadata(
            path,
            reader =>
                reader
                    .AssemblyReferences.Select(reader.GetAssemblyReference)
                    .Where(reference => reader.GetString(reference.Name) == CompilerAssemblyName)
                    .Select(reference => reference.Version)
                    .FirstOrDefault()
        );

    // Unreadable images keep their analyzers so Roslyn reports its own load failure.
    private Version? ReadMetadata(string path, Func<MetadataReader, Version?> read)
    {
        using var stream = _fileSystem.File.OpenRead(path);
        using var image = new PEReader(stream);
        try
        {
            return image.HasMetadata ? read(image.GetMetadataReader()) : null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    // MSBuild keeps Windows separators from SDK targets in evaluated property values.
    private string Normalize(string path) =>
        path.Replace('\\', _fileSystem.Path.DirectorySeparatorChar);
}
