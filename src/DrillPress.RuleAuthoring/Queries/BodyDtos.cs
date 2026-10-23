namespace DrillPress;

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

    /// <summary>If, both switch forms, conditional expressions, loops and catch filters; excludes short-circuit and null-propagating operators.</summary>
    AnyBranchOrLoop =
        If | SwitchStatement | SwitchExpression | ConditionalExpression | Loop | CatchFilter,

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

/// <summary>The kind of exception handler clause code runs in.</summary>
public enum HandlerClause
{
    /// <summary>Not in a catch or finally clause.</summary>
    None,

    /// <summary>In a catch clause, while an exception is handled.</summary>
    Catch,

    /// <summary>In a finally clause, which also runs while an exception propagates.</summary>
    Finally,
}
