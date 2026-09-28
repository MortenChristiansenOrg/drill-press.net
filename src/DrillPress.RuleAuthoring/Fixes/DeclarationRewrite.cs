using Microsoft.CodeAnalysis;

namespace DrillPress.Fixes;

/// <summary>The selected declaration and every affected declared symbol after a modifier edit.</summary>
public sealed class DeclarationRewrite
{
    internal DeclarationRewrite(
        RewriteEvidence rewrite,
        SyntaxToken removed,
        IReadOnlyList<SymbolRewrite> symbols
    )
    {
        Rewrite = rewrite;
        Removed = removed;
        Symbols = symbols;
    }

    /// <summary>The complete batch mapping and original/rewritten semantic models.</summary>
    public RewriteEvidence Rewrite { get; }

    /// <summary>The actual token removed in this original compilation membership.</summary>
    public SyntaxToken Removed { get; }

    /// <summary>All affected symbols, including every field/event declarator and complete compiler partial-symbol semantics.</summary>
    public IReadOnlyList<SymbolRewrite> Symbols { get; }
}
