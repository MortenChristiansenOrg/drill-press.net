using System.Diagnostics;
using System.Globalization;
using System.IO.Abstractions;
using DrillPress.BundleVerification;

namespace DrillPress.Benchmarks;

public class MeasuredProcess(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    public virtual async Task<MeasuredExecution> RunAsync(
        string executable,
        string[] arguments,
        string workingDirectory,
        string outputPrefix,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        outputPrefix = _fileSystem.Path.GetFullPath(outputPrefix);
        _fileSystem.Directory.CreateDirectory(_fileSystem.Path.GetDirectoryName(outputPrefix)!);
        var (output, measurement) = OperatingSystem.IsLinux()
            ? await RunLinuxAsync(
                executable,
                arguments,
                workingDirectory,
                outputPrefix,
                cancellationToken
            )
            : await RunWindowsAsync(executable, arguments, workingDirectory, cancellationToken);
        var stdout = outputPrefix + ".stdout";
        var stderr = outputPrefix + ".stderr";
        await _fileSystem.File.WriteAllBytesAsync(stdout, output.StandardOutput, cancellationToken);
        await _fileSystem.File.WriteAllBytesAsync(stderr, output.StandardError, cancellationToken);
        return new(
            executable,
            arguments.ToArray(),
            workingDirectory,
            output.ExitCode,
            stdout,
            stderr,
            measurement
        );
    }

    private async Task<(ProcessOutput Output, ProcessMeasurement Measurement)> RunLinuxAsync(
        string executable,
        string[] arguments,
        string workingDirectory,
        string outputPrefix,
        CancellationToken cancellationToken
    )
    {
        var resourcePath = outputPrefix + ".resources";
        var start = Stopwatch.GetTimestamp();
        var output = await ProcessRunner.RunAsync(
            "/usr/bin/time",
            ["--quiet", "-f", "%U\n%S\n%M", "-o", resourcePath, "--", executable, .. arguments],
            workingDirectory,
            cancellationToken,
            TimeSpan.FromMinutes(30)
        );
        var wall = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var fields = (
            await _fileSystem.File.ReadAllTextAsync(resourcePath, cancellationToken)
        ).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (
            fields.Length != 3
            || !double.TryParse(fields[0], CultureInfo.InvariantCulture, out var user)
            || !double.TryParse(fields[1], CultureInfo.InvariantCulture, out var system)
            || !long.TryParse(fields[2], CultureInfo.InvariantCulture, out var peak)
            || !double.IsFinite(user)
            || !double.IsFinite(system)
            || user < 0
            || system < 0
            || peak <= 0
        )
        {
            throw new InvalidDataException(
                "GNU time returned incomplete process resource measurements."
            );
        }

        return (
            output,
            new(
                wall,
                user * 1000,
                system * 1000,
                checked(peak * 1024),
                "process-and-waited-descendants",
                "GNU-time-child-peak-rss"
            )
        );
    }

    private static async Task<(
        ProcessOutput Output,
        ProcessMeasurement Measurement
    )> RunWindowsAsync(
        string executable,
        string[] arguments,
        string workingDirectory,
        CancellationToken cancellationToken
    )
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Repository measurements require Linux or Windows."
            );
        }

        ProcessMeasurement? measurement = null;
        var start = Stopwatch.GetTimestamp();
        var output = await ProcessRunner.RunAsync(
            executable,
            arguments,
            workingDirectory,
            cancellationToken,
            TimeSpan.FromMinutes(30),
            process =>
            {
                var wall = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                measurement = new(
                    wall,
                    process.UserProcessorTime.TotalMilliseconds,
                    process.PrivilegedProcessorTime.TotalMilliseconds,
                    ChildPeakMemory.Read(process),
                    "process-only",
                    "Windows-process-peak-working-set"
                );
            }
        );
        return (
            output,
            measurement
                ?? throw new InvalidDataException(
                    "The process exited without resource measurements."
                )
        );
    }
}
