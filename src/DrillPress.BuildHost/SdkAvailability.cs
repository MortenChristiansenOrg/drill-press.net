using System.Diagnostics;

namespace DrillPress.BuildHost;

internal static class SdkAvailability
{
    public static async Task VerifyAsync(string directory, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        start.ArgumentList.Add("--version");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the .NET SDK resolver.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdout, stderr);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            try
            {
                await Task.WhenAll(stdout, stderr);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Both cancelled readers have been observed after stopping the SDK process.
            }

            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"No compatible .NET SDK was found for '{directory}'. Install the SDK required by its global.json.");
        }
    }
}
