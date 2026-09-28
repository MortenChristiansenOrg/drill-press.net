namespace DrillPress.Queries;

/// <summary>Controls whether traversal crosses a separately executable function body.</summary>
public enum NestedFunctions
{
    /// <summary>Stop at lambda, anonymous-method and local-function boundaries.</summary>
    Exclude,

    /// <summary>Include nested executable bodies in the selected syntax scope.</summary>
    Include,
}

/// <summary>Explicit syntax categories; adding an SDK category does not change an existing selection.</summary>
[Flags]
public enum ControlFlowKinds
{
    /// <summary>No control-flow constructs.</summary>
    None = 0,

    /// <summary>If statements, including else-if continuations.</summary>
    If = 1,

    /// <summary>Switch statements.</summary>
    SwitchStatement = 2,

    /// <summary>Switch expressions.</summary>
    SwitchExpression = 4,

    /// <summary>For, foreach, while and do statements.</summary>
    Loop = 8,

    /// <summary>Conditional expressions.</summary>
    ConditionalExpression = 16,

    /// <summary>Catch filter clauses.</summary>
    CatchFilter = 32,

    /// <summary>Short-circuit Boolean operators.</summary>
    ShortCircuit = 64,

    /// <summary>Null-coalescing expressions.</summary>
    Coalesce = 128,

    /// <summary>Conditional member access.</summary>
    ConditionalAccess = 256,
}
