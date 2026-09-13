using System.Diagnostics;
using System.Text;

namespace DrillPress.BundleVerification;

public static class ProcessRunner
{
    public static async Task<ProcessOutput> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null,
        Action<Process>? afterExit = null
    )
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromMinutes(2));
        deadline.Token.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        startInfo.Environment["VSLANG"] = "1033";
        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{executable}'.");
        using var stdout = new MemoryStream();
        using var stderr = new MemoryStream();
        var drain = Task.WhenAll(
            process.StandardOutput.BaseStream.CopyToAsync(stdout),
            process.StandardError.BaseStream.CopyToAsync(stderr)
        );
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            await drain.WaitAsync(deadline.Token);
            afterExit?.Invoke(process);
            return new ProcessOutput(process.ExitCode, stdout.ToArray(), stderr.ToArray());
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            await drain;
            throw;
        }
    }

    public static void RequireSuccess(ProcessOutput result)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Child exited {result.ExitCode}.\n{Encoding.UTF8.GetString(result.StandardOutput)}{Encoding.UTF8.GetString(result.StandardError)}"
            );
        }
    }
}
