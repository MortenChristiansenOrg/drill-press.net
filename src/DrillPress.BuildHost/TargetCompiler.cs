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
        // unless CscToolPath, or a CscToolExe containing a directory, overrides it. A bare
        // CscToolExe without CscToolPath is searched on PATH, so its compiler is unknown.
        var exeDirectory = _fileSystem.Path.GetDirectoryName(toolExe) ?? "";
        if (toolExe.Length > 0 && exeDirectory.Length == 0 && toolPath.Length == 0)
            return null;
        var directory =
            toolPath.Length > 0 ? toolPath
            : tasks.Length > 0
                ? _fileSystem.Path.Combine(_fileSystem.Path.GetDirectoryName(tasks)!, "bincore")
            : targets.Length > 0 ? _fileSystem.Path.Combine(targets, "bincore")
            : "";
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
                && Languages(reader, attribute)
                    .Contains(LanguageNames.CSharp, StringComparer.OrdinalIgnoreCase)
            );

    // Analyzer assemblies may also define the attribute types themselves.
    private static (string, string)? AttributeType(MetadataReader reader, CustomAttribute attribute)
    {
        var parent = attribute.Constructor.Kind switch
        {
            HandleKind.MemberReference => reader
                .GetMemberReference((MemberReferenceHandle)attribute.Constructor)
                .Parent,
            HandleKind.MethodDefinition => reader
                .GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor)
                .GetDeclaringType(),
            _ => default(EntityHandle),
        };
        if (parent.Kind == HandleKind.TypeReference)
        {
            var reference = reader.GetTypeReference((TypeReferenceHandle)parent);
            return (reader.GetString(reference.Namespace), reader.GetString(reference.Name));
        }

        if (parent.Kind == HandleKind.TypeDefinition)
        {
            var definition = reader.GetTypeDefinition((TypeDefinitionHandle)parent);
            return (reader.GetString(definition.Namespace), reader.GetString(definition.Name));
        }

        return null;
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
        // The compiler ignores attributes whose params array is null.
        if (count < 0)
            return [];
        for (var index = 0; index < count; index++)
            languages.Add(blob.ReadSerializedString());
        return languages.OfType<string>().ToArray();
    }

    // Unreadable images keep their analyzers so Roslyn reports its own load failure.
    private Version? ReadMetadata(string path, Func<MetadataReader, Version?> read)
    {
        try
        {
            using var stream = _fileSystem.File.OpenRead(path);
            using var image = new PEReader(stream);
            return image.HasMetadata ? read(image.GetMetadataReader()) : null;
        }
        catch (Exception exception)
            when (exception is BadImageFormatException or IOException or UnauthorizedAccessException
            )
        {
            return null;
        }
    }

    // MSBuild keeps Windows separators from SDK targets in evaluated property values.
    private string Normalize(string path) =>
        path.Replace('\\', _fileSystem.Path.DirectorySeparatorChar);
}
