using System.Collections.Concurrent;
using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis;

namespace DrillPress.BuildHost;

// A worker still evaluates the complete graph after an input change. Unchanged evaluated
// compiler inputs and dependency generations can retain their generated compilation.
internal sealed class ProjectGenerationCache(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly Dictionary<string, string> _identities = [];
    private readonly ConcurrentDictionary<string, GeneratedProject> _projects = new();
    private InputFileFingerprints _files = new(fileSystem);

    internal Dictionary<ProjectId, string> Begin(Solution solution, SnapshotLoadOptions options)
    {
        _files = new(_fileSystem);
        var properties = JsonSerializer.Serialize(
            options
                .Properties.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .SelectMany(pair => new[] { pair.Key, pair.Value })
                .ToArray(),
            CompilationSnapshotJsonContext.Default.StringArray
        );
        var result = new Dictionary<ProjectId, string>();
        var activeKeys = new HashSet<string>();
        foreach (
            var group in solution.Projects.GroupBy(project => (project.FilePath, project.Name))
        )
        {
            foreach (var project in group)
            {
                var key = JsonSerializer.Serialize(
                    new[] { properties, project.FilePath ?? "", project.Name },
                    CompilationSnapshotJsonContext.Default.StringArray
                );
                activeKeys.Add(key);
                if (group.Count() != 1)
                {
                    // The workspace may hold alternate property sets with identical names.
                    result.Add(project.Id, project.Id.Id.ToString("N"));
                    continue;
                }
                if (!_identities.TryGetValue(key, out var identity))
                    _identities.Add(key, identity = Guid.NewGuid().ToString("N"));
                result.Add(project.Id, identity);
            }
        }
        foreach (var key in _identities.Keys.Where(key => !activeKeys.Contains(key)).ToArray())
            _identities.Remove(key);
        var active = result.Values.ToHashSet();
        foreach (var key in _projects.Keys.Where(key => !active.Contains(key)))
            _projects.TryRemove(key, out _);
        return result;
    }

    internal string Fingerprint(
        ProjectSnapshot input,
        EvaluatedMetadata metadata,
        IEnumerable<string> dependencies,
        CancellationToken cancellationToken
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(
            JsonSerializer.SerializeToUtf8Bytes(
                input,
                CompilationSnapshotJsonContext.Default.ProjectSnapshot
            )
        );
        foreach (var signature in dependencies)
            Append(hash, signature);
        foreach (var path in metadata.InputPaths.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(hash, path);
            Append(hash, _files.Read(path));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal GeneratedProject? Find(string contextId, string signature) =>
        _projects.TryGetValue(contextId, out var cached) && cached.Signature == signature
            ? cached
            : null;

    internal void Store(GeneratedProject project) => _projects[project.ContextId] = project;

    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }
}
