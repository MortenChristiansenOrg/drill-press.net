namespace DrillPress.Cli;

internal sealed record CliOptions(
    CliCommand Command,
    string? BuildHost,
    string Rules,
    string Target,
    string[] ExportArguments,
    bool Profile,
    bool EnableOptimizations,
    bool RefreshCoverage,
    bool SkipCoverage,
    bool ShowFixComplexity,
    IReadOnlySet<RuleFixComplexity?>? FixComplexities,
    bool ExplainCoverage,
    bool ExplainFixes,
    bool UseWorker = false,
    bool ParallelQueries = false,
    bool NativeGenerators = false
)
{
    public static bool TryParse(string[] args, out CliOptions options, out string? error)
    {
        options = null!;
        error = null;
        if (args.Length < 4 || args[0] is not ("check" or "fix" or "status" or "stop-workers"))
        {
            return false;
        }

        string? buildHost = null;
        string? rules = null;
        string? target = null;
        var exportArguments = new List<string>();
        var profile = false;
        var optimize = true;
        var refreshCoverage = false;
        var skipCoverage = false;
        var showFixComplexity = false;
        var explainCoverage = false;
        var explainFixes = false;
        var worker = false;
        var parallelQueries = false;
        var nativeGenerators = false;
        IReadOnlySet<RuleFixComplexity?>? fixComplexities = null;
        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--native-generators":
                    nativeGenerators = true;
                    break;
                case "--parallel-queries":
                    parallelQueries = true;
                    break;
                case "--worker":
                    worker = true;
                    break;
                case "--no-worker":
                    worker = false;
                    break;
                case "--build-host" when buildHost is null && index + 1 < args.Length:
                    buildHost = args[++index];
                    break;
                case "--rules" when rules is null && index + 1 < args.Length:
                    rules = args[++index];
                    break;
                case "--property" when index + 1 < args.Length && args[index + 1].IndexOf('=') > 0:
                    exportArguments.Add(args[index]);
                    exportArguments.Add(args[++index]);
                    break;
                case "--profile":
                    profile = true;
                    exportArguments.Add(args[index]);
                    break;
                case "--show-fix-complexity":
                    showFixComplexity = true;
                    break;
                case "--fix-complexity":
                    if (
                        fixComplexities is not null
                        || index + 1 >= args.Length
                        || !FixComplexityFilter.TryParse(args[++index], out fixComplexities)
                    )
                    {
                        error =
                            "--fix-complexity requires a comma-separated list of trivial, local, complex, architectural, or unspecified; specify the option once.";
                        return false;
                    }
                    break;
                case "--explain-coverage":
                    explainCoverage = true;
                    break;
                case "--explain-fixes":
                    explainFixes = true;
                    break;
                case "--refresh-coverage":
                    refreshCoverage = true;
                    break;
                case "--skip-coverage":
                    skipCoverage = true;
                    break;
                case "--no-optimization":
                    optimize = false;
                    break;
                case "--include-referenced-projects":
                case "--validate-compilation":
                    exportArguments.Add(args[index]);
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

        if (
            (buildHost is not null && string.IsNullOrWhiteSpace(buildHost))
            || string.IsNullOrWhiteSpace(rules)
            || string.IsNullOrWhiteSpace(target)
        )
        {
            return false;
        }

        if (refreshCoverage && skipCoverage)
        {
            error = "--refresh-coverage and --skip-coverage cannot be combined.";
            return false;
        }

        options = new CliOptions(
            args[0] switch
            {
                "fix" => CliCommand.Fix,
                "status" => CliCommand.Status,
                "stop-workers" => CliCommand.StopWorkers,
                _ => CliCommand.Check,
            },
            buildHost,
            rules,
            target,
            exportArguments.ToArray(),
            profile,
            optimize,
            refreshCoverage,
            skipCoverage,
            showFixComplexity,
            fixComplexities,
            explainCoverage,
            explainFixes,
            worker,
            parallelQueries,
            nativeGenerators
        );
        return true;
    }
}
