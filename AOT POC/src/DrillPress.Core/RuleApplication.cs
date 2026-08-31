using System.Collections.Immutable;
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
        if (!TryParseArguments(args, out var command, out var target, out var format))
        {
            WriteUsage();
            return 2;
        }

        try
        {
            var solution = await SolutionLoader.LoadAsync(target, cancellationToken);
            var diagnostics = rules.Evaluate(solution);
            var applied = 0;
            if (command == "fix")
            {
                applied = await ApplyFixesAsync(diagnostics, cancellationToken);
                if (applied > 0)
                {
                    solution = await SolutionLoader.LoadAsync(target, cancellationToken);
                    diagnostics = rules.Evaluate(solution);
                }
            }

            WriteDiagnostics(diagnostics, format);

            if (command == "fix" && applied > 0)
            {
                Console.Error.WriteLine($"Applied {applied} edit(s). Run check again to verify the result.");
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
        out string format)
    {
        command = args.FirstOrDefault() ?? string.Empty;
        target = string.Empty;
        format = "jsonl";
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
            else if (string.IsNullOrEmpty(target))
            {
                target = args[index];
            }
            else
            {
                return false;
            }
        }

        return !string.IsNullOrWhiteSpace(target) && format is "jsonl" or "text";
    }

    private static void WriteUsage()
    {
        Console.Error.WriteLine("Usage: <rule-bundle> <check|fix> <solution|project|file|directory|glob> [--format jsonl|text]");
    }

    private static void WriteDiagnostics(
        ImmutableArray<RuleDiagnostic> diagnostics,
        string format)
    {
        foreach (var diagnostic in diagnostics)
        {
            var text = diagnostic.Location.Document.Text;
            var linePosition = text.Lines.GetLinePosition(diagnostic.Location.Span.Start);
            var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), diagnostic.Location.Document.Path)
                .Replace(Path.DirectorySeparatorChar, '/');
            if (format == "text")
            {
                Console.Out.WriteLine(
                    $"{relativePath}:{linePosition.Line + 1}:{linePosition.Character + 1}: " +
                    $"{diagnostic.Descriptor.Id} {diagnostic.Descriptor.Message}");
                continue;
            }

            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteNumber("v", 1);
                writer.WriteString("rule", diagnostic.Descriptor.Id);
                writer.WriteString("severity", "warning");
                writer.WriteString("file", relativePath);
                writer.WriteNumber("start", diagnostic.Location.Span.Start);
                writer.WriteNumber("length", diagnostic.Location.Span.Length);
                writer.WriteNumber("line", linePosition.Line + 1);
                writer.WriteNumber("column", linePosition.Character + 1);
                writer.WriteString("message", diagnostic.Descriptor.Message);
                if (!diagnostic.Fixes.IsEmpty)
                {
                    writer.WriteStartArray("fixes");
                    foreach (var edit in diagnostic.Fixes)
                    {
                        writer.WriteStartObject();
                        writer.WriteString(
                            "file",
                            Path.GetRelativePath(Directory.GetCurrentDirectory(), edit.FilePath)
                                .Replace(Path.DirectorySeparatorChar, '/'));
                        writer.WriteNumber("start", edit.Span.Start);
                        writer.WriteNumber("length", edit.Span.Length);
                        writer.WriteString("text", edit.NewText);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }

            Console.Out.WriteLine(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length)));
        }
    }

    private static async Task<int> ApplyFixesAsync(
        ImmutableArray<RuleDiagnostic> diagnostics,
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
}
