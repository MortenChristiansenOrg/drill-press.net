using System.Diagnostics;

namespace DrillPress.Cli;

/// <summary>Runs external tools with captured console streams and stops their process tree on cancellation.</summary>
public class ChildProcessRunner
{
    /// <summary>Executes a managed DLL or native program and waits until it stops, including on cancellation.</summary>
    public virtual async Task<int> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await CaptureAsync(executable, arguments, cancellationToken);
        return result.ExitCode;
    }

    /// <summary>Drains stdout and stderr concurrently, keeping child logs out of public diagnostics.</summary>
    public virtual async Task<ChildProcessResult> CaptureAsync(
        string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var isManagedAssembly = executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var startInfo = new ProcessStartInfo(isManagedAssembly ? "dotnet" : executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new System.Text.UTF8Encoding(false, true),
            StandardErrorEncoding = new System.Text.UTF8Encoding(false, true),
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
        using var output = new MemoryStream();
        using var error = new MemoryStream();
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(output);
        var stderr = process.StandardError.BaseStream.CopyToAsync(error);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            throw;
        }

        await Task.WhenAll(stdout, stderr);
        var encoding = new System.Text.UTF8Encoding(false, true);
        return new ChildProcessResult(process.ExitCode, encoding.GetString(output.ToArray()), encoding.GetString(error.ToArray()));
    }
}
