using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DrillPress.BuildHost;

/// <summary>
/// Restarts BuildHost on the newest installed .NET runtime for a target whose SDK belongs to a newer .NET.
/// Targets on this runtime's SDK keep running on it, as a build would.
/// </summary>
internal class RuntimeRollForward
{
    // Marks the restarted process, so it reports a missing runtime instead of restarting again.
    private const string RestartedVariable = "DRILLPRESS_BUILDHOST_ROLLED_FORWARD";

    /// <summary>Whether this process already is the restart on the newest runtime.</summary>
    public virtual bool IsRolledForward =>
        Environment.GetEnvironmentVariable(RestartedVariable) == "1";

    /// <summary>
    /// Runs BuildHost with <paramref name="args"/> on the newest runtime, sharing this process's console. Only a
    /// preview <paramref name="sdk"/>, whose runtime is a prerelease, lets it choose a prerelease runtime.
    /// </summary>
    public virtual async Task<BuildHostExitCode> RestartAsync(
        string[] args,
        SelectedSdk sdk,
        CancellationToken cancellationToken
    )
    {
        // The runtime directory is <dotnet root>/shared/Microsoft.NETCore.App/<version>/.
        var root = Path.GetFullPath(
            Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..")
        );
        var start = new ProcessStartInfo(
            Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet")
        )
        {
            UseShellExecute = false,
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add("--roll-forward");
        start.ArgumentList.Add("LatestMajor");
        start.ArgumentList.Add(typeof(RuntimeRollForward).Assembly.Location);
        foreach (var argument in args)
            start.ArgumentList.Add(argument);
        start.Environment[RestartedVariable] = "1";
        if (sdk.IsPreview)
            start.Environment["DOTNET_ROLL_FORWARD_TO_PRERELEASE"] = "1";
        using var process =
            Process.Start(start)
            ?? throw new InvalidOperationException("Could not restart BuildHost.");
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception exception)
                when (exception is InvalidOperationException or System.ComponentModel.Win32Exception
                    && process.HasExited
                )
            {
                // The restarted BuildHost exited between cancellation and termination.
            }

            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        return (BuildHostExitCode)process.ExitCode;
    }
}
