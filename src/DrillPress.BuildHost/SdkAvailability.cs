using System.Diagnostics;

namespace DrillPress.BuildHost;

internal static class SdkAvailability
{
    /// <summary>Returns the SDK version that <c>dotnet</c> selects for <paramref name="directory"/>, honoring its global.json.</summary>
    public static async Task<SelectedSdk> ResolveAsync(
        string directory,
        CancellationToken cancellationToken
    )
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var probeToken = deadline.Token;
        probeToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("--version");
        using var process =
            Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the .NET SDK resolver.");
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
            catch (Exception exception)
                when (exception is InvalidOperationException or System.ComponentModel.Win32Exception
                    && process.HasExited
                )
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
                throw new TimeoutException(
                    $"SDK discovery timed out after 30 seconds for '{directory}'."
                );
            }

            throw;
        }

        var output = (await stdout).Trim();
        // Preview SDKs append a label, such as 11.0.100-rc.2.25502.107, which System.Version can't hold.
        var label = output.IndexOf('-');
        if (
            process.ExitCode != 0
            || !Version.TryParse(label < 0 ? output : output[..label], out var version)
        )
        {
            throw new InvalidOperationException(
                $"No compatible .NET SDK was found for '{directory}'. Install the SDK required by its global.json."
            );
        }

        return new SelectedSdk(version, label >= 0);
    }
}
