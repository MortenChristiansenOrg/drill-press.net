namespace DrillPress.Engine;

/// <summary>Hosts a compiled rule set behind the executable rule-bundle contract.</summary>
public static class RuleApplication
{
    /// <summary>
    /// Executes the rule-bundle command, writes compact diagnostics, and returns the
    /// clean, findings, or failure exit code understood by the coordinator.
    /// </summary>
    public static async Task<RuleExitCode> RunAsync(
        RuleSet rules,
        string[] args,
        TextWriter? standardOutput = null,
        TextWriter? standardError = null,
        CancellationToken cancellationToken = default)
    {
        standardOutput ??= Console.Out;
        standardError ??= Console.Error;
        if (args is not ["check", var snapshotPath])
        {
            await standardError.WriteLineAsync("Usage: <rule-bundle> check <snapshot>");
            return RuleExitCode.Failure;
        }

        try
        {
            var diagnostics = await AnalysisEngine.AnalyzeAsync(rules, snapshotPath, cancellationToken);
            WriteDiagnostics(diagnostics, standardOutput);
            return diagnostics.Count == 0 ? RuleExitCode.Clean : RuleExitCode.Findings;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await standardError.WriteLineAsync($"drillpress-rules: {exception.Message}");
            return RuleExitCode.Failure;
        }
    }

    private static void WriteDiagnostics(
        IReadOnlyList<RuleDiagnostic> diagnostics,
        TextWriter standardOutput)
    {
        foreach (var ruleGroup in diagnostics.GroupBy(diagnostic => diagnostic.Descriptor))
        {
            standardOutput.WriteLine($"{ruleGroup.Key.Id} {ruleGroup.Key.Message}");
            foreach (var fileGroup in ruleGroup.GroupBy(
                         diagnostic => DisplayPath(diagnostic.Location.FilePath)))
            {
                standardOutput.WriteLine(fileGroup.Key);
                foreach (var diagnostic in fileGroup)
                {
                    standardOutput.WriteLine(
                        diagnostic.Location.Column == 1
                            ? $"  {diagnostic.Location.Line}"
                            : $"  {diagnostic.Location.Line}:{diagnostic.Location.Column}");
                }
            }
        }
    }

    private static string DisplayPath(string path)
    {
        var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }
}
