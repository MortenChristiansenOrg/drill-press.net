using DrillPress.Cli;

namespace DrillPress.UnitTests.TestInfrastructure;

/// <summary>Answers each child process from a script so tests can tell BuildHost, Git and rule-bundle calls apart.</summary>
internal sealed class ScriptedChildProcessRunner(
    Func<string, IReadOnlyList<string>, Task<ChildProcessResult>> execute
) : ChildProcessRunner
{
    public override Task<ChildProcessResult> CaptureAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    ) => execute(executable, arguments);
}
