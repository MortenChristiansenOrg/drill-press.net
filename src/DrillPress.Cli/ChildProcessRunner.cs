using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;

namespace DrillPress.Cli;

/// <summary>Runs external tools with bounded console capture and stops their process tree on failure or cancellation.</summary>
public class ChildProcessRunner
{
    private readonly int _maximumStandardOutputBytes;
    private readonly int _maximumStandardErrorBytes;

    /// <summary>Limits each captured pipe independently; oversized responses fail before protocol validation.</summary>
    /// <param name="maximumStandardOutputBytes">Maximum internal response or loader log size; defaults to 64 MiB.</param>
    /// <param name="maximumStandardErrorBytes">Maximum operational error output; defaults to 8 MiB.</param>
    public ChildProcessRunner(
        int maximumStandardOutputBytes = 64 * 1024 * 1024,
        int maximumStandardErrorBytes = 8 * 1024 * 1024
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumStandardOutputBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumStandardErrorBytes);
        _maximumStandardOutputBytes = maximumStandardOutputBytes;
        _maximumStandardErrorBytes = maximumStandardErrorBytes;
    }

    /// <summary>Executes a managed DLL or native program and waits until it stops, including on cancellation.</summary>
    public virtual async Task<int> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        var result = await CaptureAsync(executable, arguments, cancellationToken);
        return result.ExitCode;
    }

    /// <summary>Drains stdout and stderr concurrently, keeping child logs out of public diagnostics.</summary>
    public virtual async Task<ChildProcessResult> CaptureAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process =
            Process.Start(CreateStartInfo(executable, arguments))
            ?? throw new InvalidOperationException($"Could not start '{executable}'.");
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var failure = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var stdout = CaptureStreamAsync(
            process.StandardOutput.BaseStream,
            _maximumStandardOutputBytes,
            "standard output",
            failure,
            shutdown.Token
        );
        var stderr = CaptureStreamAsync(
            process.StandardError.BaseStream,
            _maximumStandardErrorBytes,
            "standard error",
            failure,
            shutdown.Token
        );
        var drain = Task.WhenAll(stdout, stderr);
        var exit = process.WaitForExitAsync(shutdown.Token);
        var completion = Task.WhenAll(drain, exit);
        try
        {
            // Observe either pipe's failure immediately, even while the other pipe and process are still running.
            if (await Task.WhenAny(failure.Task, completion) == failure.Task)
            {
                ExceptionDispatchInfo.Capture(await failure.Task).Throw();
            }

            await completion;
        }
        catch
        {
            await shutdown.CancelAsync();
            await StopAsync(process);
            await completion.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            throw;
        }

        var encoding = new UTF8Encoding(false, true);
        return new ChildProcessResult(
            process.ExitCode,
            encoding.GetString(await stdout),
            encoding.GetString(await stderr)
        );
    }

    private static ProcessStartInfo CreateStartInfo(
        string executable,
        IReadOnlyList<string> arguments
    )
    {
        var isManagedAssembly = executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var startInfo = new ProcessStartInfo(isManagedAssembly ? "dotnet" : executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (isManagedAssembly)
        {
            startInfo.ArgumentList.Add(executable);
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task StopAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception)
            when ((exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                && process.HasExited
            )
        {
            // A process can exit between the state check and termination.
        }

        await process.WaitForExitAsync(CancellationToken.None);
    }

    private static async Task<byte[]> CaptureStreamAsync(
        Stream stream,
        int maximumBytes,
        string name,
        TaskCompletionSource<Exception> failure,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var captured = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, cancellationToken)) != 0)
            {
                if (count > maximumBytes - captured.Length)
                {
                    throw new InvalidDataException($"Child {name} exceeded {maximumBytes} bytes.");
                }

                captured.Write(buffer, 0, count);
            }

            return captured.ToArray();
        }
        catch (Exception exception)
        {
            failure.TrySetResult(exception);
            throw;
        }
    }
}
