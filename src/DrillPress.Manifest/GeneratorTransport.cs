using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace DrillPress.Manifest;

internal sealed class GeneratorTransport(IFileSystem fileSystem, string directory)
{
    private const int ProtocolVersion = 1;
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly string _directory = fileSystem.Path.GetFullPath(directory);

    internal bool HasCapabilities => _fileSystem.File.Exists(Path("capabilities.json"));

    internal Task WriteCapabilitiesAsync(
        GeneratorDescription[] generators,
        CancellationToken cancellationToken
    ) =>
        WriteAsync(
            Path("capabilities.json"),
            new GeneratorCapabilities(ComponentVersion.Current, ProtocolVersion, generators),
            GeneratorPlanJsonContext.Default.GeneratorCapabilities,
            cancellationToken
        );

    internal async Task<GeneratorDescription[]> ReadCapabilitiesAsync(
        CancellationToken cancellationToken
    )
    {
        var capabilities = await ReadAsync(
            Path("capabilities.json"),
            GeneratorPlanJsonContext.Default.GeneratorCapabilities,
            cancellationToken
        );
        ComponentVersion.RequireMatch(capabilities.ProductVersion, "Generator bundle");
        if (capabilities.ProtocolVersion != ProtocolVersion)
            throw new InvalidDataException("Generator protocol versions do not match.");
        return capabilities.Generators;
    }

    internal IEnumerable<string> Plans() =>
        _fileSystem.Directory.EnumerateFiles(_directory, "*.plan.json");

    internal Task WritePlanAsync(GeneratorProjectPlan plan, CancellationToken cancellationToken) =>
        WriteAsync(
            ContextPath(plan.Project.ContextId, "plan.json"),
            plan,
            GeneratorPlanJsonContext.Default.GeneratorProjectPlan,
            cancellationToken
        );

    internal Task<GeneratorProjectPlan> ReadPlanAsync(
        string path,
        CancellationToken cancellationToken
    ) => ReadAsync(path, GeneratorPlanJsonContext.Default.GeneratorProjectPlan, cancellationToken);

    internal Task WriteRequestAsync(
        string contextId,
        GeneratorFallbackRequest request,
        CancellationToken cancellationToken
    ) =>
        WriteAsync(
            ContextPath(contextId, "request.json"),
            request,
            GeneratorPlanJsonContext.Default.GeneratorFallbackRequest,
            cancellationToken
        );

    internal async Task<GeneratorFallbackRequest> ReadRequestAsync(
        string contextId,
        CancellationToken cancellationToken
    )
    {
        var path = ContextPath(contextId, "request.json");
        await WaitAsync(path, cancellationToken);
        return await ReadAsync(
            path,
            GeneratorPlanJsonContext.Default.GeneratorFallbackRequest,
            cancellationToken
        );
    }

    internal Task WriteResponseAsync(
        string contextId,
        GeneratorFallbackResponse response,
        CancellationToken cancellationToken
    ) =>
        WriteAsync(
            ContextPath(contextId, "response.json"),
            response,
            GeneratorPlanJsonContext.Default.GeneratorFallbackResponse,
            cancellationToken
        );

    internal async Task<GeneratorFallbackResponse> ReadResponseAsync(
        string contextId,
        CancellationToken cancellationToken
    )
    {
        var path = ContextPath(contextId, "response.json");
        await WaitAsync(path, cancellationToken);
        return await ReadAsync(
            path,
            GeneratorPlanJsonContext.Default.GeneratorFallbackResponse,
            cancellationToken
        );
    }

    internal MetadataReferenceSnapshot WriteReference(string contextId, byte[] bytes)
    {
        var path = ContextPath(contextId, "dll");
        var temporary = path + ".tmp";
        _fileSystem.File.WriteAllBytes(temporary, bytes);
        _fileSystem.File.Move(temporary, path, overwrite: true);
        return new MetadataReferenceSnapshot(
            path,
            Convert.ToHexString(SHA256.HashData(bytes)),
            [],
            false,
            0
        );
    }

    private string ContextPath(string contextId, string suffix)
    {
        if (
            contextId.Length == 0
            || contextId.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')
            )
        )
            throw new InvalidDataException("Invalid generator context identity.");
        return Path(contextId + "." + suffix);
    }

    private string Path(string name) => _fileSystem.Path.Combine(_directory, name);

    private async Task WaitAsync(string path, CancellationToken cancellationToken)
    {
        while (!_fileSystem.File.Exists(path))
            await Task.Delay(5, cancellationToken);
    }

    private async Task<T> ReadAsync<T>(
        string path,
        JsonTypeInfo<T> type,
        CancellationToken cancellationToken
    )
    {
        var bytes = await _fileSystem.File.ReadAllBytesAsync(path, cancellationToken);
        return JsonSerializer.Deserialize(bytes, type)
            ?? throw new InvalidDataException("Empty generator message.");
    }

    private async Task WriteAsync<T>(
        string path,
        T value,
        JsonTypeInfo<T> type,
        CancellationToken cancellationToken
    )
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await _fileSystem.File.WriteAllBytesAsync(
                temporary,
                JsonSerializer.SerializeToUtf8Bytes(value, type),
                cancellationToken
            );
            _fileSystem.File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            _fileSystem.File.Delete(temporary);
        }
    }
}
