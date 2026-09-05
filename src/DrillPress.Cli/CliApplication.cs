using System.Diagnostics;

namespace DrillPress.Cli;

/// <summary>
/// Coordinates target export and compiled rule execution without loading MSBuild,
/// Roslyn, or rule assemblies into the CLI process.
/// </summary>
public static class CliApplication
{
    /// <summary>
    /// Executes the public check command and returns its typed process outcome.
    /// </summary>
    public static async Task<CliExitCode> RunAsync(
        string[] args,
        TextWriter? standardError = null,
        CancellationToken cancellationToken = default)
    {
        standardError ??= Console.Error;
        if (!CliOptions.TryParse(args, out var options))
        {
            await standardError.WriteLineAsync(
                "Usage: drillpress check --build-host <path> --rules <path> <project.csproj>");
            return CliExitCode.Failure;
        }

        try
        {
            var temporaryDirectory = Directory.CreateTempSubdirectory("drillpress-");
            try
            {
                var snapshotPath = Path.Combine(temporaryDirectory.FullName, "compilation.snapshot.json");
                var buildHostExitCode = await RunManagedProcessAsync(
                    options.BuildHost,
                    ["export", options.Target, snapshotPath],
                    cancellationToken);
                if (buildHostExitCode != (int)CliExitCode.Clean)
                {
                    return CliExitCode.Failure;
                }

                var ruleExitCode = await RunManagedProcessAsync(
                    options.Rules,
                    ["check", snapshotPath],
                    cancellationToken);
                return ruleExitCode switch
                {
                    (int)CliExitCode.Clean => CliExitCode.Clean,
                    (int)CliExitCode.Findings => CliExitCode.Findings,
                    _ => CliExitCode.Failure,
                };
            }
            finally
            {
                temporaryDirectory.Delete(recursive: true);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await standardError.WriteLineAsync($"drillpress: {exception.Message}");
            return CliExitCode.Failure;
        }
    }

    private static async Task<int> RunManagedProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var isManagedAssembly = executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var startInfo = new ProcessStartInfo(isManagedAssembly ? "dotnet" : executable)
        {
            UseShellExecute = false,
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
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        return process.ExitCode;
    }

    private sealed record CliOptions(string BuildHost, string Rules, string Target)
    {
        public static bool TryParse(string[] args, out CliOptions options)
        {
            options = null!;
            if (args.Length < 6 || args[0] != "check")
            {
                return false;
            }

            string? buildHost = null;
            string? rules = null;
            string? target = null;
            for (var index = 1; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--build-host" when buildHost is null && index + 1 < args.Length:
                        buildHost = args[++index];
                        break;
                    case "--rules" when rules is null && index + 1 < args.Length:
                        rules = args[++index];
                        break;
                    default:
                        if (target is not null || args[index].StartsWith('-'))
                        {
                            return false;
                        }

                        target = args[index];
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(buildHost) ||
                string.IsNullOrWhiteSpace(rules) ||
                string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            options = new CliOptions(buildHost, rules, target);
            return true;
        }
    }
}
