using System.Text.Json.Serialization;

namespace DrillPress.Manifest;

[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(WorkerResponse))]
internal partial class WorkerJsonContext : JsonSerializerContext;
