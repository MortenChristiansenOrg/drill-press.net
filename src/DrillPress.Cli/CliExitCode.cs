namespace DrillPress.Cli;

/// <summary>Defines the process outcomes returned by the Drill Press coordinator.</summary>
public enum CliExitCode
{
    /// <summary>No rule violations were found.</summary>
    Clean = 0,

    /// <summary>One or more rule violations were found.</summary>
    Findings = 1,

    /// <summary>The command input was invalid or a child tool failed.</summary>
    Failure = 2,
}
