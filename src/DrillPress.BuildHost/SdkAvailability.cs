using System.Diagnostics;

namespace DrillPress.BuildHost;

internal static class SdkAvailability
{
    public static async Task VerifyAsync(string directory, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var probeToken = deadline.Token;
        probeToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        start.ArgumentList.Add("--version");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the .NET SDK resolver.");
        var stdout = process.StandardOutput.ReadToEndAsync(probeToken);
        var stderr = process.StandardError.ReadToEndAsync(probeToken);
        try
        {
            await process.WaitForExitAsync(probeToken);
            await Task.WhenAll(stdout, stderr);
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception && process.HasExited)
            {
                // The SDK process exited between the observation and termination.
            }

            await process.WaitForExitAsync(CancellationToken.None);
            try
            {
                await Task.WhenAll(stdout, stderr);
            }
            catch (OperationCanceledException) when (probeToken.IsCancellationRequested)
            {
                // Both cancelled readers have been observed after stopping the SDK process.
            }

            if (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"SDK discovery timed out after 30 seconds for '{directory}'.");
            }

            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"No compatible .NET SDK was found for '{directory}'. Install the SDK required by its global.json.");
        }
    }
}
