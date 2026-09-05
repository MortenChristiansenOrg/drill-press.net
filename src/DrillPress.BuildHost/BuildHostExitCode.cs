namespace DrillPress.BuildHost;

/// <summary>Defines the process outcomes returned by the BuildHost.</summary>
public enum BuildHostExitCode
{
    /// <summary>The project snapshot was exported successfully.</summary>
    Success = 0,

    /// <summary>The command input was invalid or snapshot export failed.</summary>
    Failure = 2,
}
