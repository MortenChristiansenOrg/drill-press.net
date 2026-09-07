using System.Diagnostics;

namespace DrillPress.Cli;

/// <summary>Runs external tools with inherited console streams and stops their process tree on cancellation.</summary>
public class ChildProcessRunner
{
    /// <summary>Executes a managed DLL or native program and waits until it stops, including on cancellation.</summary>
    public virtual async Task<int> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var isManagedAssembly = executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var startInfo = new ProcessStartInfo(isManagedAssembly ? "dotnet" : executable)
        {
            UseShellExecute = false,
        };
        if (isManagedAssembly)
        {
            startInfo.ArgumentList.Add(executable);
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{executable}'.");
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        return process.ExitCode;
    }
}
