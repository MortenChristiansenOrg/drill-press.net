using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>Exact, fully constructed method symbols resolved in the original compilation; reduced methods are normalized to their declarations.</summary>
/// <param name="Before">The explicitly approved original overload.</param>
/// <param name="After">The explicitly approved destination overload.</param>
public sealed record MethodPair(IMethodSymbol Before, IMethodSymbol After);
