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
    /// <param name="projectDirectory">Resolves relative tool paths.</param>
    /// <param name="property">Reads an evaluated MSBuild property; empty when undefined.</param>
    public string? CompilerAssemblyPath(string projectDirectory, Func<string, string> property)
    {
        var toolPath = Normalize(property("CscToolPath"));
        var toolExe = Normalize(property("CscToolExe"));
        var tasks = Normalize(property("RoslynTasksAssembly"));
        var targets = Normalize(property("RoslynTargetsPath"));
        // Csc runs bincore beside its task assembly (SDK 8 and 9 only define RoslynTargetsPath)
        // unless CscToolPath, or a CscToolExe containing a directory, overrides it.
        var directory =
            toolPath.Length > 0 ? toolPath
            : tasks.Length > 0
                ? _fileSystem.Path.Combine(_fileSystem.Path.GetDirectoryName(tasks)!, "bincore")
            : targets.Length > 0 ? _fileSystem.Path.Combine(targets, "bincore")
            : "";
        var exeDirectory = _fileSystem.Path.GetDirectoryName(toolExe) ?? "";
        directory = _fileSystem.Path.Combine(directory, exeDirectory);
        return directory.Length == 0
            ? null
            : _fileSystem.Path.GetFullPath(
                _fileSystem.Path.Combine(directory, CompilerAssemblyName + ".dll"),
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

    // Like the compiler, only assemblies declaring C# analyzers or generators are checked.
    private Version? ReferencedCompiler(string path) =>
        ReadMetadata(
            path,
            reader =>
                HasCSharpAnalyzers(reader)
                    ? reader
                        .AssemblyReferences.Select(reader.GetAssemblyReference)
                        .Where(reference =>
                            reader.GetString(reference.Name) == CompilerAssemblyName
                        )
                        .Select(reference => reference.Version)
                        .FirstOrDefault()
                    : null
        );

    private static bool HasCSharpAnalyzers(MetadataReader reader) =>
        reader
            .TypeDefinitions.SelectMany(type =>
                reader
                    .GetTypeDefinition(type)
                    .GetCustomAttributes()
                    .Select(reader.GetCustomAttribute)
            )
            .Any(attribute =>
                AttributeType(reader, attribute)
                    is
                        ("Microsoft.CodeAnalysis.Diagnostics", "DiagnosticAnalyzerAttribute")
                        or
                        ("Microsoft.CodeAnalysis", "GeneratorAttribute")
                && Languages(reader, attribute).Contains(LanguageNames.CSharp)
            );

    private static (string, string)? AttributeType(MetadataReader reader, CustomAttribute attribute)
    {
        if (attribute.Constructor.Kind != HandleKind.MemberReference)
            return null;
        var parent = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
        if (parent.Kind != HandleKind.TypeReference)
            return null;
        var type = reader.GetTypeReference((TypeReferenceHandle)parent);
        return (reader.GetString(type.Namespace), reader.GetString(type.Name));
    }

    // Reads (string firstLanguage, params string[] additionalLanguages); a generator without arguments targets C#.
    private static string[] Languages(MetadataReader reader, CustomAttribute attribute)
    {
        var blob = reader.GetBlobReader(attribute.Value);
        if (blob.ReadUInt16() != 1)
            return [];
        if (blob.RemainingBytes <= 2)
            return [LanguageNames.CSharp];
        var languages = new List<string?> { blob.ReadSerializedString() };
        var count = blob.ReadInt32();
        for (var index = 0; index < count; index++)
            languages.Add(blob.ReadSerializedString());
        return languages.OfType<string>().ToArray();
    }

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
