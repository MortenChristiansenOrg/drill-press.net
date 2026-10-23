using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>The selected declaration and every affected declared symbol after a modifier is removed or added.</summary>
public sealed class ModifierChange
{
    internal ModifierChange(
        RewriteEvidence rewrite,
        SyntaxToken removed,
        IReadOnlyList<SymbolRewrite> symbols,
        SyntaxToken added = default
    )
    {
        Rewrite = rewrite;
        Removed = removed;
        Added = added;
        Symbols = symbols;
    }

    /// <summary>The added modifier in authoring vocabulary, or null for a removal.</summary>
    public Modifier? AddedModifier =>
        Added.RawKind != 0 && Enum.IsDefined(typeof(Modifier), Added.RawKind)
            ? (Modifier)Added.RawKind
            : null;

    /// <summary>The token added in the rewritten declaration; default for a removal.</summary>
    public SyntaxToken Added { get; }

    /// <summary>The removed modifier in authoring vocabulary, when represented by the Modifier enumeration.</summary>
    public Modifier? RemovedModifier =>
        Removed.RawKind != 0 && Enum.IsDefined(typeof(Modifier), Removed.RawKind)
            ? (Modifier)Removed.RawKind
            : null;

    /// <summary>The complete batch mapping and original/rewritten semantic models.</summary>
    public RewriteEvidence Rewrite { get; }

    /// <summary>The actual token removed in this original compilation membership; default for an addition.</summary>
    public SyntaxToken Removed { get; }

    /// <summary>All affected symbols, including every field/event declarator and complete compiler partial-symbol semantics.</summary>
    public IReadOnlyList<SymbolRewrite> Symbols { get; }
}
