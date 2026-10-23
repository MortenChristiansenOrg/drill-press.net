using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>Corresponding declarations in the original and fully rewritten compilation.</summary>
/// <param name="Before">The original declared symbol, including merged partial semantics.</param>
/// <param name="After">The mapped declared symbol after all edits.</param>
public sealed record SymbolRewrite(ISymbol Before, ISymbol After);

/// <summary>A renamed symbol in one affected compilation, after every reference has been checked to bind to it.</summary>
/// <param name="Context">The complete rewritten compilation and edit batch.</param>
/// <param name="Before">The original symbol in this compilation.</param>
/// <param name="After">The renamed symbol in the rewritten compilation.</param>
/// <param name="References">How many references, besides declarations, were renamed in this compilation.</param>
public sealed record RenameChange(
    RewriteContext Context,
    ISymbol Before,
    ISymbol After,
    int References
);
