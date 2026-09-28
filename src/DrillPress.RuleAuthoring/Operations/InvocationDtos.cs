using Microsoft.CodeAnalysis;

namespace DrillPress.Operations;

/// <summary>How the selected member obtains a receiver, independent of conditional-access spelling.</summary>
public enum ReceiverKind
{
    /// <summary>An ordinary static call has no receiver.</summary>
    None,

    /// <summary>An explicit or implicit instance receiver.</summary>
    Instance,

    /// <summary>The normalized first parameter of an extension declaration.</summary>
    Extension,
}

/// <summary>A declaration parameter's values; empty expanded params groups remain distinguishable from missing parameters.</summary>
/// <param name="Parameter">The parameter on the normalized selected declaration.</param>
/// <param name="Values">Its explicit, expanded or defaulted values in source order.</param>
public sealed record ParameterArguments(
    IParameterSymbol Parameter,
    IReadOnlyList<CodeArgument> Values
);

/// <summary>A retained input's evaluation position; this inventory does not assert purity or guaranteed execution.</summary>
/// <param name="Value">The evaluated source expression or implicit this receiver.</param>
/// <param name="Parameter">The associated parameter, absent for ordinary instance receivers.</param>
/// <param name="IsReceiver">Whether this entry is the call receiver.</param>
public sealed record CallInput(CodeExpression Value, IParameterSymbol? Parameter, bool IsReceiver);
