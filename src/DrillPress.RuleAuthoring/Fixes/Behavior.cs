namespace DrillPress;

/// <summary>Individual bounded invariants. No combination claims complete behavioral equivalence.</summary>
[Flags]
public enum Behavior
{
    /// <summary>Retained operand binding; already checked by expression replacement.</summary>
    Bindings = 1,

    /// <summary>Each registered input appears once; does not establish order or conditional execution.</summary>
    EvaluationCounts = 2,

    /// <summary>Straight-line observable evaluation sequence under the bounded operation model.</summary>
    EvaluationSequence = 4,

    /// <summary>Bounded null-receiver behavior.</summary>
    NullReceiverBehavior = 8,

    /// <summary>Declared accessibility of rewritten declarations.</summary>
    Accessibility = 16,

    /// <summary>Compiler declaration identity.</summary>
    Identity = 32,

    /// <summary>Declaration contract and modifiers.</summary>
    Contract = 64,
}
