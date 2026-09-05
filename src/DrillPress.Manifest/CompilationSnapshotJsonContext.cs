using System.Text.Json.Serialization;

namespace DrillPress.Manifest;

/// <summary>Provides generated JSON metadata for the internal snapshot contract.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CompilationSnapshot))]
public sealed partial class CompilationSnapshotJsonContext : JsonSerializerContext;
