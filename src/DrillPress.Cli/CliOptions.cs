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
    bool ShowFixComplexity,
    IReadOnlySet<RuleFixComplexity?>? FixComplexities
)
{
    public static bool TryParse(string[] args, out CliOptions options, out string? error)
    {
        options = null!;
        error = null;
        if (args.Length < 4 || args[0] is not ("check" or "fix"))
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
        var showFixComplexity = false;
        IReadOnlySet<RuleFixComplexity?>? fixComplexities = null;
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
                case "--refresh-coverage":
                    refreshCoverage = true;
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

        options = new CliOptions(
            args[0] == "fix" ? CliCommand.Fix : CliCommand.Check,
            buildHost,
            rules,
            target,
            exportArguments.ToArray(),
            profile,
            optimize,
            refreshCoverage,
            showFixComplexity,
            fixComplexities
        );
        return true;
    }
}
