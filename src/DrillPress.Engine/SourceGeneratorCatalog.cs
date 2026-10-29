using DrillPress.Manifest;
using Microsoft.CodeAnalysis;

namespace DrillPress.Engine;

/// <summary>Declares the exact source generators linked into a rule executable, allowing capture and analysis to share compiler work.</summary>
/// <remarks>Derive a parameterless catalogue for a bundle and identify it with <see cref="SourceGeneratorCatalogAttribute"/>. Generators absent from the catalogue run in the managed capture process.</remarks>
public abstract class SourceGeneratorCatalog
{
    private readonly Dictionary<GeneratorDescription, ISourceGenerator> _generators;

    /// <summary>Collects statically constructed generators; a generator instance is shared between project drivers as in an ordinary Roslyn workspace.</summary>
    protected SourceGeneratorCatalog(IEnumerable<CompiledSourceGenerator> generators)
    {
        _generators = generators.ToDictionary(item => item.Description, item => item.Generator);
    }

    internal GeneratorDescription[] Descriptions => _generators.Keys.ToArray();

    internal ISourceGenerator? Find(GeneratorDescription description) =>
        _generators.GetValueOrDefault(description);
}
