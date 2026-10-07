using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>The selected declaration and every affected declared symbol after a modifier edit.</summary>
public sealed class ModifierChange
{
    internal ModifierChange(
        RewriteEvidence rewrite,
        SyntaxToken removed,
        IReadOnlyList<SymbolRewrite> symbols
    )
    {
        Rewrite = rewrite;
        Removed = removed;
        Symbols = symbols;
    }

    /// <summary>The removed modifier in authoring vocabulary, when represented by the Modifier enumeration.</summary>
    public Modifier? RemovedModifier =>
        Enum.IsDefined(typeof(Modifier), Removed.RawKind) ? (Modifier)Removed.RawKind : null;

    /// <summary>The complete batch mapping and original/rewritten semantic models.</summary>
    public RewriteEvidence Rewrite { get; }

    /// <summary>The actual token removed in this original compilation membership.</summary>
    public SyntaxToken Removed { get; }

    /// <summary>All affected symbols, including every field/event declarator and complete compiler partial-symbol semantics.</summary>
    public IReadOnlyList<SymbolRewrite> Symbols { get; }
}
