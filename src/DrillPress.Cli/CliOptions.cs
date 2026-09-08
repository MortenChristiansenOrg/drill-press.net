namespace DrillPress.Cli;

internal sealed record CliOptions(CliCommand Command, string BuildHost, string Rules, string Target, string[] ExportArguments)
{
    public static bool TryParse(string[] args, out CliOptions options)
    {
        options = null!;
        if (args.Length < 6 || args[0] is not ("check" or "fix"))
        {
            return false;
        }

        string? buildHost = null;
        string? rules = null;
        string? target = null;
        var exportArguments = new List<string>();
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

        if (string.IsNullOrWhiteSpace(buildHost) ||
            string.IsNullOrWhiteSpace(rules) ||
            string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        options = new CliOptions(args[0] == "fix" ? CliCommand.Fix : CliCommand.Check, buildHost, rules, target, exportArguments.ToArray());
        return true;
    }
}
