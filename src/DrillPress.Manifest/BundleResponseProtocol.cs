using System.Text.Json;

namespace DrillPress.Manifest;

/// <summary>Serializes the internal process response with generated, NativeAOT-compatible metadata.</summary>
public static class BundleResponseProtocol
{
    /// <summary>Exact version understood by the coordinator and bundle.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Produces deterministic UTF-8 JSON without a BOM or trailing newline.</summary>
    public static byte[] Serialize(BundleResponse response) =>
        JsonSerializer.SerializeToUtf8Bytes(
            response,
            CompilationSnapshotJsonContext.Default.BundleResponse
        );

    /// <summary>Rejects invalid JSON, missing members, and responses belonging to another request.</summary>
    public static ValidatedResult Read(string json, CompilationSnapshot snapshot)
    {
        var response =
            JsonSerializer.Deserialize(json, CompilationSnapshotJsonContext.Default.BundleResponse)
            ?? throw new InvalidDataException("Missing bundle response.");
        return new BundleResponseValidator().Validate(snapshot, response);
    }
}
