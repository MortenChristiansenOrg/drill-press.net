using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace DrillPress;

public sealed record CompilationManifest(
    int Version,
    string TargetPath,
    DateTimeOffset CreatedAtUtc,
    ImmutableArray<ManifestProject> Projects,
    ImmutableArray<ManifestMessage> Messages)
{
    public const int CurrentVersion = 2;
}

public sealed record ManifestProject(
    string Id,
    string Name,
    string AssemblyName,
    string ProjectPath,
    bool IsTestProject,
    int LanguageVersion,
    int DocumentationMode,
    int SourceCodeKind,
    ImmutableArray<string> PreprocessorSymbols,
    int OutputKind,
    int NullableContextOptions,
    int OptimizationLevel,
    int Platform,
    int GeneralDiagnosticOption,
    int WarningLevel,
    bool AllowUnsafe,
    bool CheckOverflow,
    bool Deterministic,
    ImmutableDictionary<string, int> SpecificDiagnosticOptions,
    ImmutableDictionary<string, int> GlobalCompilerDiagnosticOptions,
    ImmutableArray<ManifestDocument> Documents,
    ImmutableArray<ManifestDocument> AdditionalDocuments,
    ImmutableArray<ManifestDocument> AnalyzerConfigDocuments,
    ImmutableArray<string> MetadataReferences,
    ImmutableArray<string> ProjectReferences,
    int CompilerErrorCount);

public sealed record ManifestDocument(
    string Path,
    string Text,
    bool IsGenerated,
    ImmutableDictionary<string, int> CompilerDiagnosticOptions);

public sealed record ManifestMessage(
    string Kind,
    string Message,
    string? ProjectPath = null);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false)]
[JsonSerializable(typeof(CompilationManifest))]
public sealed partial class CompilationManifestJsonContext : JsonSerializerContext;
