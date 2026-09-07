namespace DrillPress.Cli;

/// <summary>The external process boundary used by the coordinator.</summary>
public interface IChildProcessRunner
{
    /// <summary>Executes a managed DLL or native program and waits until it has stopped, including on cancellation.</summary>
    Task<int> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}
