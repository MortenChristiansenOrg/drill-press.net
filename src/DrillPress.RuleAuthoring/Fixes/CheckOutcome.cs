namespace DrillPress;

/// <summary>A bounded proof's result with the reason it did not hold, for explaining withheld fixes.</summary>
internal readonly record struct CheckOutcome(ProofResult Result, string? Reason)
{
    internal static CheckOutcome Proven => new(ProofResult.Proven, null);

    internal static CheckOutcome Unknown(string reason) => new(ProofResult.Unknown, reason);

    internal static CheckOutcome Disproven(string reason) => new(ProofResult.Disproven, reason);

    internal bool Holds => Result == ProofResult.Proven;
}
