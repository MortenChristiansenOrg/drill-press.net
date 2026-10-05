using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>The result of resolving a configured identity in one evaluated compilation.</summary>
public enum TypeAvailabilityStatus
{
    /// <summary>No contextual type satisfies the complete configured identity.</summary>
    Missing,

    /// <summary>Exactly one contextual type satisfies the configured identity.</summary>
    Available,

    /// <summary>Multiple contextual types satisfy the identity; none is selected.</summary>
    Ambiguous,
}

/// <summary>Read-only contextual availability, independent of package references or source usage.</summary>
/// <param name="Status">Whether resolution found zero, one, or multiple matching identities.</param>
/// <param name="Type">The unique matching symbol, including symbols exposed through extern aliases; otherwise null.</param>
public sealed record TypeAvailability(TypeAvailabilityStatus Status, ITypeSymbol? Type);
