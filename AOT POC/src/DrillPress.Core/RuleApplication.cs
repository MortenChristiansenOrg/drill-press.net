using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

public static class RuleApplication
{
    public static async Task<int> RunAsync(
        RuleSet rules,
        string[] args,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseArguments(args, out var command, out var target, out var outputOptions, out var profile))
        {
            WriteUsage();
            return 2;
        }

        try
        {
            if (command == "fix" && target.EndsWith(".drillpress.json", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "A compilation manifest is an immutable snapshot. Fix the original target, then export a new manifest.");
            }

            var totalStarted = Stopwatch.GetTimestamp();
            var loadStarted = Stopwatch.GetTimestamp();
            var solution = await SolutionLoader.LoadAsync(target, cancellationToken);
            var loadElapsed = Stopwatch.GetElapsedTime(loadStarted);
            var evaluationStarted = Stopwatch.GetTimestamp();
            var diagnostics = rules.Evaluate(
                solution,
                profile
                    ? static (phase, elapsed) => Console.Error.WriteLine(
                        $"drillpress: rule-phase: {phase}={elapsed.TotalSeconds:F2}s")
                    : null);
            var evaluationElapsed = Stopwatch.GetElapsedTime(evaluationStarted);
            var applied = 0;
            var fixElapsed = TimeSpan.Zero;
            var reloadElapsed = TimeSpan.Zero;
            var reevaluationElapsed = TimeSpan.Zero;
            if (command == "fix")
            {
                var fixStarted = Stopwatch.GetTimestamp();
                applied = await ApplyFixesAsync(AggregateDiagnostics(diagnostics), cancellationToken);
                fixElapsed = Stopwatch.GetElapsedTime(fixStarted);
                if (applied > 0)
                {
                    var reloadStarted = Stopwatch.GetTimestamp();
                    solution = await SolutionLoader.LoadAsync(target, cancellationToken);
                    reloadElapsed = Stopwatch.GetElapsedTime(reloadStarted);
                    var reevaluationStarted = Stopwatch.GetTimestamp();
                    diagnostics = rules.Evaluate(
                        solution,
                        profile
                            ? static (phase, elapsed) => Console.Error.WriteLine(
                                $"drillpress: rule-phase: recheck-{phase}={elapsed.TotalSeconds:F2}s")
                            : null);
                    reevaluationElapsed = Stopwatch.GetElapsedTime(reevaluationStarted);
                }
            }

            var outputStarted = Stopwatch.GetTimestamp();
            WriteDiagnostics(AggregateDiagnostics(diagnostics), outputOptions);
            var outputElapsed = Stopwatch.GetElapsedTime(outputStarted);

            if (command == "fix" && applied > 0)
            {
                Console.Error.WriteLine($"Applied {applied} edit(s). Run check again to verify the result.");
            }

            if (profile)
            {
                Console.Error.WriteLine(
                    $"drillpress: phases: load={loadElapsed.TotalSeconds:F2}s, " +
                    $"evaluate={evaluationElapsed.TotalSeconds:F2}s, " +
                    $"fix={fixElapsed.TotalSeconds:F2}s, " +
                    $"reload={reloadElapsed.TotalSeconds:F2}s, " +
                    $"reevaluate={reevaluationElapsed.TotalSeconds:F2}s, " +
                    $"output={outputElapsed.TotalSeconds:F2}s, " +
                    $"total={Stopwatch.GetElapsedTime(totalStarted).TotalSeconds:F2}s");
            }

            return diagnostics.IsEmpty ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"drillpress: {exception.Message}");
            return 2;
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string command,
        out string target,
        out OutputOptions outputOptions,
        out bool profile)
    {
        command = args.FirstOrDefault() ?? string.Empty;
        target = string.Empty;
        var format = "llm";
        var details = false;
        var includeContexts = false;
        outputOptions = new OutputOptions(format, details, includeContexts);
        profile = false;
        if (command is not ("check" or "fix"))
        {
            return false;
        }

        for (var index = 1; index < args.Length; index++)
        {
            if (args[index] == "--format" && index + 1 < args.Length)
            {
                format = args[++index];
            }
            else if (args[index] == "--profile")
            {
                profile = true;
            }
            else if (args[index] == "--details")
            {
                details = true;
            }
            else if (args[index] == "--include-contexts")
            {
                includeContexts = true;
            }
            else if (string.IsNullOrEmpty(target))
            {
                target = args[index];
            }
            else
            {
                return false;
            }
        }

        outputOptions = new OutputOptions(format, details, includeContexts);
        return !string.IsNullOrWhiteSpace(target) &&
               format is "llm" or "jsonl" or "text" &&
               (!details || format == "jsonl");
    }

    private static void WriteUsage()
    {
        Console.Error.WriteLine(
            "Usage: <rule-bundle> <check|fix> <solution|project|file|directory|glob> " +
            "[--format llm|jsonl|text] [--details] [--include-contexts] [--profile]");
    }

    private static void WriteDiagnostics(
        ImmutableArray<OutputDiagnostic> diagnostics,
        OutputOptions options)
    {
        if (options.Format is "llm" or "text")
        {
            WriteLlmDiagnostics(diagnostics, options.IncludeContexts);
            return;
        }

        foreach (var diagnostic in diagnostics)
        {
            var text = diagnostic.Diagnostic.Location.Document.Text;
            var linePosition = text.Lines.GetLinePosition(diagnostic.Location.Span.Start);

            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("rule", diagnostic.Descriptor.Id);
                writer.WriteString("file", diagnostic.RelativePath);
                writer.WriteNumber("line", linePosition.Line + 1);
                writer.WriteNumber("column", linePosition.Character + 1);
                writer.WriteString("message", diagnostic.Descriptor.Message);
                if (options.Details)
                {
                    writer.WriteNumber("start", diagnostic.Location.Span.Start);
                    writer.WriteNumber("length", diagnostic.Location.Span.Length);
                    if (!diagnostic.Fixes.IsEmpty)
                    {
                        writer.WriteStartArray("fixes");
                        foreach (var edit in diagnostic.Fixes)
                        {
                            writer.WriteStartObject();
                            var editPath = GetRelativePath(edit.FilePath);
                            if (!PathComparer.Equals(editPath, diagnostic.RelativePath))
                            {
                                writer.WriteString("file", editPath);
                            }

                            writer.WriteNumber("start", edit.Span.Start);
                            writer.WriteNumber("length", edit.Span.Length);
                            writer.WriteString("text", edit.NewText);
                            writer.WriteEndObject();
                        }

                        writer.WriteEndArray();
                    }
                }

                if (options.IncludeContexts && !diagnostic.Contexts.IsEmpty)
                {
                    writer.WriteStartArray("contexts");
                    foreach (var context in diagnostic.Contexts)
                    {
                        writer.WriteStringValue(context);
                    }

                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }

            Console.Out.WriteLine(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length)));
        }
    }

    private static void WriteLlmDiagnostics(
        ImmutableArray<OutputDiagnostic> diagnostics,
        bool includeContexts)
    {
        foreach (var ruleGroup in diagnostics.GroupBy(static diagnostic => diagnostic.Descriptor.Id))
        {
            var descriptor = ruleGroup.First().Descriptor;
            Console.Out.WriteLine($"{descriptor.Id} {descriptor.Message}");
            foreach (var fileGroup in ruleGroup.GroupBy(static diagnostic => diagnostic.RelativePath))
            {
                Console.Out.WriteLine(fileGroup.Key);
                foreach (var diagnostic in fileGroup)
                {
                    var linePosition = diagnostic.Diagnostic.Location.Document.Text.Lines.GetLinePosition(
                        diagnostic.Location.Span.Start);
                    Console.Out.Write(diagnostic.Fixes.IsEmpty ? "  " : "  +");
                    Console.Out.Write(linePosition.Line + 1);
                    if (linePosition.Character > 0)
                    {
                        Console.Out.Write($":{linePosition.Character + 1}");
                    }
                    if (includeContexts && !diagnostic.Contexts.IsEmpty)
                    {
                        Console.Out.Write($" [{string.Join(',', diagnostic.Contexts)}]");
                    }

                    Console.Out.WriteLine();
                }
            }
        }
    }

    private static ImmutableArray<OutputDiagnostic> AggregateDiagnostics(
        ImmutableArray<RuleDiagnostic> diagnostics) =>
        diagnostics
            .GroupBy(static diagnostic => new DiagnosticKey(
                diagnostic.Descriptor.Id,
                NormalizePath(diagnostic.Location.Document.Path),
                diagnostic.Location.Span.Start,
                diagnostic.Location.Span.Length,
                diagnostic.Descriptor.Message))
            .Select(static group =>
            {
                var grouped = group.ToImmutableArray();
                var first = grouped[0];
                var commonFixes = first.Fixes
                    .Where(edit => grouped.Skip(1).All(diagnostic => diagnostic.Fixes.Contains(edit)))
                    .Distinct()
                    .ToImmutableArray();
                var contexts = grouped
                    .Select(static diagnostic => GetProjectContext(diagnostic.Location.Document.Project))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static context => context, StringComparer.Ordinal)
                    .ToImmutableArray();
                return new OutputDiagnostic(
                    first with { Fixes = commonFixes },
                    GetRelativePath(first.Location.Document.Path),
                    contexts);
            })
            .OrderBy(static diagnostic => diagnostic.Descriptor.Id, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.RelativePath, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Location.Span.Start)
            .ToImmutableArray();

    private static string GetProjectContext(ProjectModel project) =>
        $"{GetRelativePath(project.Path)}|{project.Name}";

    private static string GetRelativePath(string path) =>
        Path.GetRelativePath(Directory.GetCurrentDirectory(), path)
            .Replace(Path.DirectorySeparatorChar, '/');

    private static string NormalizePath(string path) => OperatingSystem.IsWindows()
        ? Path.GetFullPath(path).ToUpperInvariant()
        : Path.GetFullPath(path);

    private static async Task<int> ApplyFixesAsync(
        ImmutableArray<OutputDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var applied = 0;
        var fileGroups = diagnostics.SelectMany(static diagnostic => diagnostic.Fixes)
            .GroupBy(static edit => edit.FilePath, PathComparer)
            .Select(group => (Path: group.Key, Edits: group.OrderByDescending(static edit => edit.Span.Start).ToArray()))
            .ToArray();
        foreach (var fileGroup in fileGroups)
        {
            ValidateEdits(fileGroup.Edits);
        }

        foreach (var fileGroup in fileGroups)
        {
            var source = await File.ReadAllTextAsync(fileGroup.Path, cancellationToken);
            var builder = new StringBuilder(source);
            foreach (var edit in fileGroup.Edits)
            {
                builder.Remove(edit.Span.Start, edit.Span.Length);
                builder.Insert(edit.Span.Start, edit.NewText);
                applied++;
            }

            var temporaryPath = fileGroup.Path + ".drillpress.tmp";
            await File.WriteAllTextAsync(temporaryPath, builder.ToString(), new UTF8Encoding(false), cancellationToken);
            File.Move(temporaryPath, fileGroup.Path, true);
        }

        return applied;
    }

    private static void ValidateEdits(IReadOnlyList<TextEdit> edits)
    {
        for (var index = 1; index < edits.Count; index++)
        {
            var later = edits[index - 1];
            var earlier = edits[index];
            if (earlier.Span.End > later.Span.Start)
            {
                throw new InvalidOperationException(
                    $"Fixes overlap in '{earlier.FilePath}' at offsets {earlier.Span.Start} and {later.Span.Start}.");
            }
        }
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed record OutputOptions(string Format, bool Details, bool IncludeContexts);

    private sealed record DiagnosticKey(string Rule, string Path, int Start, int Length, string Message);

    private sealed record OutputDiagnostic(
        RuleDiagnostic Diagnostic,
        string RelativePath,
        ImmutableArray<string> Contexts)
    {
        public RuleDescriptor Descriptor => Diagnostic.Descriptor;

        public SourceLocation Location => Diagnostic.Location;

        public ImmutableArray<TextEdit> Fixes => Diagnostic.Fixes;
    }
}
