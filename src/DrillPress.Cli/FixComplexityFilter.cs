namespace DrillPress.Cli;

internal static class FixComplexityFilter
{
    public static bool TryParse(string value, out IReadOnlySet<RuleFixComplexity?> complexities)
    {
        var selected = new HashSet<RuleFixComplexity?>();
        complexities = selected;
        foreach (var level in value.Split(','))
        {
            switch (level.Trim().ToLowerInvariant())
            {
                case "trivial":
                    selected.Add(RuleFixComplexity.Trivial);
                    break;
                case "local":
                    selected.Add(RuleFixComplexity.Local);
                    break;
                case "complex":
                    selected.Add(RuleFixComplexity.Complex);
                    break;
                case "architectural":
                    selected.Add(RuleFixComplexity.Architectural);
                    break;
                case "unspecified":
                    selected.Add(null);
                    break;
                default:
                    return false;
            }
        }

        return true;
    }
}
