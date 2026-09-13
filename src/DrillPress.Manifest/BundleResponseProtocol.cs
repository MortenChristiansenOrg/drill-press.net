using System.Text.Json;

namespace DrillPress.Manifest;

/// <summary>Serializes the internal process response with generated, NativeAOT-compatible metadata.</summary>
public static class BundleResponseProtocol
{
    /// <summary>Exact version understood by the coordinator and bundle.</summary>
    public const int CurrentVersion = 2;

    /// <summary>Produces deterministic UTF-8 JSON without a BOM or trailing newline.</summary>
    public static byte[] Serialize(BundleResponse response) =>
        JsonSerializer.SerializeToUtf8Bytes(
            response,
            CompilationSnapshotJsonContext.Default.BundleResponse
        );

    /// <summary>Rejects invalid JSON, missing members, and responses belonging to another request.</summary>
    public static ValidatedResult Read(string json, CompilationSnapshot snapshot)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        ComponentVersion.RequireMatch(
            root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("productVersion", out var productVersion)
            && productVersion.ValueKind == JsonValueKind.String
                ? productVersion.GetString()!
                : "missing",
            "Rule bundle"
        );
        var response =
            root.Deserialize(CompilationSnapshotJsonContext.Default.BundleResponse)
            ?? throw new InvalidDataException("Missing bundle response.");
        return new BundleResponseValidator().Validate(snapshot, response);
    }
}
