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
    bool ParallelQueries = false,
    bool StreamCompilation = false,
    string? Baseline = null
)
{
    /// <summary>Rule IDs to report, or null for every rule.</summary>
    public string[]? Select { get; init; }

    /// <summary>Rule IDs to leave out.</summary>
    public string[] Ignore { get; init; } = [];

    /// <summary>Full-path globs of source files that report nothing.</summary>
    public string[] Exclude { get; init; } = [];

    /// <summary>
    /// Parses the arguments and applies repository defaults beneath them: an explicit rules source or target replaces
    /// the file's, command-line properties follow the file's, <c>--select</c> replaces its selection and ignored rules,
    /// and <c>--ignore</c> and <c>--exclude</c> add to it.
    /// </summary>
    /// <exception cref="FileNotFoundException">An explicit config file does not exist.</exception>
    /// <exception cref="InvalidDataException">The config file is invalid.</exception>
    public static bool TryParse(
        string[] args,
        RepositoryConfigFile configFile,
        out CliOptions options,
        out string? error
    )
    {
        options = null!;
        error = null;
        if (args.Length == 0 || args[0] is not ("check" or "fix" or "status"))
        {
            return false;
        }

        string? buildHost = null;
        string? rules = null;
        string? target = null;
        string? baseline = null;
        string? config = null;
        List<string>? select = null;
        var ignore = new List<string>();
        var exclude = new List<string>();
        var exportArguments = new List<string>();
        var properties = new List<string>();
        var profile = false;
        var optimize = true;
        var refreshCoverage = false;
        var skipCoverage = false;
        var showFixComplexity = false;
        var explainCoverage = false;
        var explainFixes = false;
        var parallelQueries = false;
        var streamCompilation = false;
        IReadOnlySet<RuleFixComplexity?>? fixComplexities = null;
        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--stream-compilation":
                    streamCompilation = true;
                    break;
                case "--parallel-queries":
                    parallelQueries = true;
                    break;
                case "--build-host" when buildHost is null && index + 1 < args.Length:
                    buildHost = args[++index];
                    break;
                case "--baseline" when baseline is null && index + 1 < args.Length:
                    baseline = args[++index];
                    break;
                case "--rules" when rules is null && index + 1 < args.Length:
                    rules = args[++index];
                    break;
                case "--config" when config is null && index + 1 < args.Length:
                    config = args[++index];
                    break;
                case "--select":
                case "--ignore":
                    var option = args[index];
                    if (index + 1 >= args.Length || !TryParseRuleIds(args[++index], out var ids))
                    {
                        error = $"{option} requires comma-separated rule IDs.";
                        return false;
                    }
                    (option == "--select" ? select ??= [] : ignore).AddRange(ids);
                    break;
                case "--exclude"
                    when index + 1 < args.Length && !string.IsNullOrWhiteSpace(args[index + 1]):
                    exclude.Add(args[++index]);
                    break;
                case "--property" when index + 1 < args.Length && args[index + 1].IndexOf('=') > 0:
                    properties.Add(args[++index]);
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
            || (rules is not null && string.IsNullOrWhiteSpace(rules))
            || (target is not null && string.IsNullOrWhiteSpace(target))
            || (baseline is not null && string.IsNullOrWhiteSpace(baseline))
            || (config is not null && string.IsNullOrWhiteSpace(config))
        )
        {
            return false;
        }

        if (refreshCoverage && skipCoverage)
        {
            error = "--refresh-coverage and --skip-coverage cannot be combined.";
            return false;
        }

        var defaults = configFile.Load(config);
        rules ??= defaults?.Rules;
        target ??= defaults?.Target;
        if (rules is null || target is null)
        {
            if (defaults is not null)
                error =
                    $"Pass {(rules is null ? "--rules <path>" : "a target")} or set '{(rules is null ? "rules" : "target")}' in {defaults.DisplayPath}.";
            return false;
        }

        // Loose files and globs accept no MSBuild properties, so the file's properties apply to project targets only.
        var configured = IsLooseSource(target) ? [] : defaults?.Properties ?? [];
        string[] export =
        [
            .. configured
                .Select(property => $"{property.Key}={property.Value}")
                .Concat(properties)
                .SelectMany(assignment => new[] { "--property", assignment }),
            .. exportArguments,
        ];

        options = new CliOptions(
            args[0] switch
            {
                "fix" => CliCommand.Fix,
                "status" => CliCommand.Status,
                _ => CliCommand.Check,
            },
            buildHost,
            rules,
            target,
            export,
            profile,
            optimize,
            refreshCoverage,
            skipCoverage,
            showFixComplexity,
            fixComplexities,
            explainCoverage,
            explainFixes,
            parallelQueries,
            streamCompilation,
            baseline
        )
        {
            Select = select?.Distinct().ToArray() ?? defaults?.Select,
            // An explicit selection replaces the file's selection and the rules it ignores.
            Ignore = [.. (select is null ? defaults?.Ignore ?? [] : []).Concat(ignore).Distinct()],
            Exclude =
            [
                .. (defaults?.Exclude ?? [])
                    .Concat(exclude.Select(configFile.ExclusionGlob))
                    .Distinct(),
            ],
        };
        return true;
    }

    private static bool TryParseRuleIds(string value, out string[] ids)
    {
        ids = value.Split(',', StringSplitOptions.TrimEntries);
        return ids.All(id => id.Length > 0);
    }

    private static bool IsLooseSource(string target) =>
        target.IndexOfAny(['*', '?']) >= 0
        || target.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
}
