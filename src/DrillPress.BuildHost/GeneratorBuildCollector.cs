using System.Collections.Concurrent;
using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DrillPress.BuildHost;

internal sealed class GeneratorBuildCollector(IFileSystem fileSystem)
{
    private readonly ConcurrentDictionary<GeneratorDescription, GeneratorBuildEntry> _generators =
        new();
    private readonly ConcurrentDictionary<string, byte> _paths = new();
    private readonly ConcurrentDictionary<string, Lazy<string>> _fingerprints = new();

    internal void Add(ISourceGenerator[] generators, IReadOnlyList<AnalyzerReference> references)
    {
        foreach (var reference in references)
            if (reference.FullPath is { } path)
                _paths.TryAdd(path, 0);
        foreach (var generator in generators)
        {
            var type = generator.GetGeneratorType();
            var path = type.Assembly.Location;
            var hash = _fingerprints
                .GetOrAdd(
                    path,
                    name => new Lazy<string>(() =>
                        Convert.ToHexString(SHA256.HashData(fileSystem.File.ReadAllBytes(name)))
                    )
                )
                .Value;
            var description = new GeneratorDescription(
                type.FullName!,
                type.Assembly.FullName!,
                hash
            );
            _generators.TryAdd(
                description,
                new GeneratorBuildEntry(
                    description,
                    path,
                    type.GetInterfaces()
                        .Any(contract =>
                            contract.FullName == typeof(IIncrementalGenerator).FullName
                        ),
                    type.IsVisible
                        && !type.IsAbstract
                        && !type.ContainsGenericParameters
                        && type.GetConstructor(Type.EmptyTypes) is not null
                )
            );
        }
    }

    internal async Task WriteAsync(string path, CancellationToken cancellationToken)
    {
        var inventory = new GeneratorBuildInventory(
            ComponentVersion.Current,
            _generators
                .Values.OrderBy(item => item.Description.AssemblyName)
                .ThenBy(item => item.Description.TypeName)
                .ToArray(),
            _paths.Keys.Order().ToArray()
        );
        await fileSystem.File.WriteAllBytesAsync(
            path,
            JsonSerializer.SerializeToUtf8Bytes(
                inventory,
                GeneratorPlanJsonContext.Default.GeneratorBuildInventory
            ),
            cancellationToken
        );
    }
}
