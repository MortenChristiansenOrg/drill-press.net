namespace DrillPress.Engine;

public static class RuleApplication
{
    public static async Task<int> RunAsync(
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
            return 2;
        }

        try
        {
            var diagnostics = await AnalysisEngine.AnalyzeAsync(rules, snapshotPath, cancellationToken);
            WriteDiagnostics(diagnostics, standardOutput);
            return diagnostics.Count == 0 ? 0 : 1;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await standardError.WriteLineAsync($"drillpress-rules: {exception.Message}");
            return 2;
        }
    }

    private static void WriteDiagnostics(
        IReadOnlyList<RuleDiagnostic> diagnostics,
        TextWriter standardOutput)
    {
        foreach (var ruleGroup in diagnostics.GroupBy(
                     diagnostic => diagnostic.Descriptor,
                     EqualityComparer<RuleDescriptor>.Default))
        {
            standardOutput.WriteLine($"{ruleGroup.Key.Id} {ruleGroup.Key.Message}");
            foreach (var fileGroup in ruleGroup.GroupBy(
                         diagnostic => DisplayPath(diagnostic.Location.FilePath),
                         StringComparer.Ordinal))
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
