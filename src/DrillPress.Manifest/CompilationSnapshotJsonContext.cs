using System.Text.Json.Serialization;

namespace DrillPress.Manifest;

/// <summary>Provides NativeAOT-compatible JSON metadata for snapshots, bundle responses, and profile events.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(CompilationSnapshot))]
[JsonSerializable(typeof(BundleResponse))]
[JsonSerializable(typeof(ProfileEvent))]
public sealed partial class CompilationSnapshotJsonContext : JsonSerializerContext;
