using System.Text.Json.Serialization;

namespace DrillPress.Manifest;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
)]
[JsonSerializable(typeof(GeneratorProjectPlan))]
[JsonSerializable(typeof(GeneratorDescription[]))]
[JsonSerializable(typeof(GeneratorCapabilities))]
[JsonSerializable(typeof(GeneratorFallbackRequest))]
[JsonSerializable(typeof(GeneratorFallbackResponse))]
[JsonSerializable(typeof(GeneratorBuildInventory))]
internal partial class GeneratorPlanJsonContext : JsonSerializerContext;
