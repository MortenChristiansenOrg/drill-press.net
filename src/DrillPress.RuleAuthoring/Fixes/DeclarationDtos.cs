using Microsoft.CodeAnalysis;

namespace DrillPress.Fixes;

/// <summary>Corresponding declarations in the original and fully rewritten compilation.</summary>
/// <param name="Before">The original declared symbol, including merged partial semantics.</param>
/// <param name="After">The mapped declared symbol after all edits.</param>
public sealed record SymbolRewrite(ISymbol Before, ISymbol After);
