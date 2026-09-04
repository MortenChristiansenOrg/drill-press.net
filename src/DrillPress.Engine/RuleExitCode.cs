namespace DrillPress.Engine;

/// <summary>Defines the process outcomes returned by a compiled rule bundle.</summary>
public enum RuleExitCode
{
    /// <summary>No rule violations were found.</summary>
    Clean = 0,

    /// <summary>One or more rule violations were found.</summary>
    Findings = 1,

    /// <summary>The command input was invalid or analysis failed.</summary>
    Failure = 2,
}
