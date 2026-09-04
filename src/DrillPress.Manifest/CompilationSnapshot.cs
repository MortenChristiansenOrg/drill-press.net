using System.Text.Json;
using System.Text.Json.Serialization;

namespace DrillPress.Manifest;

public sealed record CompilationSnapshot(
    string Magic,
    int FormatVersion,
    ProjectSnapshot[] Projects)
{
    public const string ExpectedMagic = "drillpress-compilation";
    public const int CurrentFormatVersion = 1;

    public static CompilationSnapshot Create(params ProjectSnapshot[] projects) =>
        new(ExpectedMagic, CurrentFormatVersion, projects);

    public static async Task<CompilationSnapshot> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var snapshot = await JsonSerializer.DeserializeAsync(
                stream,
                CompilationSnapshotJsonContext.Default.CompilationSnapshot,
                cancellationToken)
            ?? throw new InvalidDataException($"Compilation snapshot '{path}' is empty.");
        snapshot.Validate();
        return snapshot;
    }

    public async Task WriteAsync(string path, CancellationToken cancellationToken = default)
    {
        Validate();
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(
            stream,
            this,
            CompilationSnapshotJsonContext.Default.CompilationSnapshot,
            cancellationToken);
    }

    private void Validate()
    {
        if (!StringComparer.Ordinal.Equals(Magic, ExpectedMagic))
        {
            throw new InvalidDataException("The input is not a Drill Press compilation snapshot.");
        }

        if (FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Compilation snapshot format {FormatVersion} is not supported; expected {CurrentFormatVersion}.");
        }
    }
}

public sealed record ProjectSnapshot(
    string Name,
    string AssemblyName,
    string ProjectPath,
    int LanguageVersion,
    int OutputKind,
    int NullableContextOptions,
    string[] PreprocessorSymbols,
    DocumentSnapshot[] Documents,
    string[] MetadataReferences);

public sealed record DocumentSnapshot(string Path, string Text, bool IsGenerated);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CompilationSnapshot))]
public sealed partial class CompilationSnapshotJsonContext : JsonSerializerContext;
