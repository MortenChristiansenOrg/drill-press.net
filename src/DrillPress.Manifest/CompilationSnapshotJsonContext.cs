using System.Text.Json.Serialization;

namespace DrillPress.Manifest;

/// <summary>Provides generated JSON metadata for the internal snapshot contract.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(CompilationSnapshot))]
[JsonSerializable(typeof(BundleResponse))]
public sealed partial class CompilationSnapshotJsonContext : JsonSerializerContext;
