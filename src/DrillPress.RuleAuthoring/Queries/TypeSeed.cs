using Microsoft.CodeAnalysis;

namespace DrillPress.Queries;

/// <summary>A source-anchored static type observation; an unresolved type remains a seed so incompleteness is visible.</summary>
public sealed class TypeSeed(ICodeElement anchor, ITypeSymbol? type, object? evidence = null)
    : ICodeElement
{
    /// <summary>The selected use site or declaration supplying this type.</summary>
    public ICodeElement Anchor { get; } = anchor;

    /// <summary>The actual constructed static type before argument conversion; null denotes missing binding.</summary>
    public ITypeSymbol? Type { get; } = type;

    /// <summary>Original attribute, argument or consumer evidence, without enumerating every reachability path.</summary>
    public object? Evidence { get; } = evidence;

    /// <summary>The seed's compiler context, retained independently from reached declaration ownership.</summary>
    public AnalysisSource Source { get; } =
        anchor.Source
        ?? throw new ArgumentException("A type seed requires a source anchor.", nameof(anchor));

    /// <summary>The seed's diagnostic anchor.</summary>
    public SourceLocation Location => Anchor.Location;
}
