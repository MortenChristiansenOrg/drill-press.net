using System.IO.Abstractions;
using DrillPress.Manifest;

namespace DrillPress.Cli;

internal sealed class GeneratorProcessPipeline(IFileSystem fileSystem, ChildProcessRunner runner)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    internal async Task<GeneratorProcessResult> CaptureAsync(
        CliOptions options,
        string snapshotPath,
        IReadOnlyList<string> ruleFlags,
        CancellationToken cancellationToken
    )
    {
        var directory = _fileSystem.Path.Combine(
            _fileSystem.Path.GetDirectoryName(snapshotPath)!,
            "generation-" + Guid.NewGuid().ToString("N")
        );
        _fileSystem.Directory.CreateDirectory(directory);
        var inputPath = _fileSystem.Path.Combine(directory, "input.snapshot.json");
        var generatedPath = _fileSystem.Path.Combine(directory, "generated.snapshot.json");
        var transport = new GeneratorTransport(_fileSystem, directory);
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var rules = runner.CaptureAsync(
            options.Rules,
            ["check-generated", directory, inputPath, generatedPath, .. ruleFlags],
            shutdown.Token
        );
        Task<ChildProcessResult>? export = null;
        try
        {
            while (!transport.HasCapabilities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (rules.IsCompleted)
                {
                    var stopped = await rules;
                    throw new IOException(
                        $"Rule generator startup failed: {stopped.StandardError}"
                    );
                }
                await Task.Delay(5, shutdown.Token);
            }
            export = runner.CaptureAsync(
                options.BuildHost!,
                [
                    "export",
                    options.Target,
                    inputPath,
                    "--generation-plan",
                    directory,
                    .. options.ExportArguments,
                ],
                shutdown.Token
            );
            var first = await Task.WhenAny(export, rules);
            var completed = await first;
            if (
                completed.ExitCode is not (0 or 1)
                || (ReferenceEquals(first, export) && completed.ExitCode != 0)
            )
                throw new IOException($"Generation pipeline failed: {completed.StandardError}");
            await Task.WhenAll(export, rules);
            if (export.Result.ExitCode != 0)
                throw new IOException(
                    $"BuildHost exited {export.Result.ExitCode}: {export.Result.StandardError}"
                );
            return new GeneratorProcessResult(export.Result, rules.Result, generatedPath);
        }
        finally
        {
            await shutdown.CancelAsync();
            await ObserveAsync(rules);
            if (export is not null)
                await ObserveAsync(export);
        }
    }

    private static async Task ObserveAsync(Task task) =>
        await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
}
