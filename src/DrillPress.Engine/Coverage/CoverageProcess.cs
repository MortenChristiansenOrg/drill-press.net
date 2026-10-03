using System.Diagnostics;

namespace DrillPress.Engine;

internal class CoverageProcess
{
    internal virtual async Task<string> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string directory,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        info.Environment["DOTNET_COVERAGE_TELEMETRY_OPTOUT"] = "1";
        using var process =
            Process.Start(info)
            ?? throw new InvalidOperationException("Coverage process could not start.");
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var output = ReadAsync(process.StandardOutput, shutdown.Token);
        var error = ReadAsync(process.StandardError, shutdown.Token);
        var exit = process.WaitForExitAsync(shutdown.Token);
        try
        {
            var pending = new List<Task> { output, error, exit };
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending);
                await completed;
                pending.Remove(completed);
            }
        }
        catch
        {
            await shutdown.CancelAsync();
            await StopAsync(process);
            await Task.WhenAll(output, error, exit)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            throw;
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Coverage collection failed ({process.ExitCode}): {(await error + await output).ReplaceLineEndings(" ")}"
            );
        return await output;
    }

    private static async Task StopAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception exception)
            when ((exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                && process.HasExited
            )
        {
            // The process can exit between inspection and termination.
        }
        await process.WaitForExitAsync(CancellationToken.None);
    }

    private static async Task<string> ReadAsync(
        StreamReader reader,
        CancellationToken cancellationToken
    )
    {
        var buffer = new char[4096];
        var output = new System.Text.StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            if (output.Length + count > 1024 * 1024)
                throw new InvalidDataException("Coverage process output exceeded 1 MiB.");
            output.Append(buffer, 0, count);
        }
        return output.ToString();
    }
}
