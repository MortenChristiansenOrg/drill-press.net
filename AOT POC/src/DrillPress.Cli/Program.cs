using System.Diagnostics;
using System.Text;
using System.Text.Json;

if (!Options.TryParse(args, out var options))
{
    Options.WriteUsage();
    return 2;
}

try
{
    if (!NeedsBuildHost(options.Target))
    {
        return await RunRuleBundleAsync(options, options.Command, options.Target, options.Format);
    }

    var buildHost = ResolveBuildHost(options.BuildHost);
    var temporaryDirectory = Path.Combine(Path.GetTempPath(), "drillpress", Guid.NewGuid().ToString("N"));
    var manifest = Path.Combine(temporaryDirectory, "target.drillpress.json");
    Directory.CreateDirectory(temporaryDirectory);
    try
    {
        if (await ExportAsync(options, buildHost, manifest) != 0)
        {
            return 2;
        }

        if (options.Command == "check")
        {
            return await RunRuleBundleAsync(options, "check", manifest, options.Format);
        }

        var initialCheck = await RunRuleBundleCapturedAsync(options, manifest);
        if (initialCheck.ExitCode is not (0 or 1))
        {
            return 2;
        }

        var edits = ParseFixes(initialCheck.StandardOutput);
        if (edits.Count == 0)
        {
            if (options.Format == "jsonl" && options.Details && !options.IncludeContexts)
            {
                Console.Out.Write(initialCheck.StandardOutput);
                return initialCheck.ExitCode;
            }

            return await RunRuleBundleAsync(options, "check", manifest, options.Format);
        }

        var applied = await ApplyFixesAsync(edits);
        Console.Error.WriteLine($"drillpress: applied {applied} edit(s); rebuilding the compilation snapshot.");
        if (await ExportAsync(options, buildHost, manifest) != 0)
        {
            return 2;
        }

        return await RunRuleBundleAsync(options, "check", manifest, options.Format);
    }
    finally
    {
        Directory.Delete(temporaryDirectory, recursive: true);
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine($"drillpress: {exception.Message}");
    return 2;
}

static bool NeedsBuildHost(string target)
{
    if (target.EndsWith(".drillpress.json", StringComparison.OrdinalIgnoreCase) ||
        target.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
        target.IndexOfAny(['*', '?']) >= 0)
    {
        return false;
    }

    return Directory.Exists(target) ||
           target.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
           target.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ||
           target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
}

static string ResolveBuildHost(string? configuredPath)
{
    var candidates = new[]
    {
        configuredPath,
        Environment.GetEnvironmentVariable("DRILLPRESS_BUILD_HOST"),
        Path.Combine(AppContext.BaseDirectory, "DrillPress.BuildHost.dll"),
        Path.Combine(AppContext.BaseDirectory, "DrillPress.BuildHost"),
    };
    foreach (var candidate in candidates)
    {
        if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
        {
            return Path.GetFullPath(candidate);
        }
    }

    throw new InvalidOperationException(
        "SDK-evaluated targets require DrillPress.BuildHost. Use --build-host <path> or set DRILLPRESS_BUILD_HOST.");
}

static async Task<int> ExportAsync(Options options, string buildHost, string manifest)
{
    var arguments = new List<string> { "export", options.Target, manifest };
    foreach (var property in options.Properties)
    {
        arguments.Add("--property");
        arguments.Add(property);
    }

    if (options.AllowCompilerErrors)
    {
        arguments.Add("--allow-compiler-errors");
    }

    if (options.Fast)
    {
        arguments.Add("--skip-compiler-diagnostics");
    }

    var result = await RunProcessAsync(buildHost, arguments, captureOutput: false);
    if (result.ExitCode != 0)
    {
        Console.Error.WriteLine(
            $"drillpress: BuildHost rejected the target with exit code {result.ExitCode}; rule evaluation was skipped.");
    }

    return result.ExitCode;
}

static Task<ProcessResult> RunRuleBundleCapturedAsync(Options options, string manifest) =>
    RunProcessAsync(
        options.Rules,
        CreateRuleArguments(options, "check", manifest, "jsonl", details: true, includeContexts: false),
        captureOutput: true);

static async Task<int> RunRuleBundleAsync(Options options, string command, string target, string format)
{
    var result = await RunProcessAsync(
        options.Rules,
        CreateRuleArguments(options, command, target, format, options.Details, options.IncludeContexts),
        captureOutput: false);
    return result.ExitCode;
}

static IReadOnlyList<string> CreateRuleArguments(
    Options options,
    string command,
    string target,
    string format,
    bool details,
    bool includeContexts)
{
    var arguments = new List<string> { command, target, "--format", format };
    if (details)
    {
        arguments.Add("--details");
    }

    if (includeContexts)
    {
        arguments.Add("--include-contexts");
    }

    if (options.Profile)
    {
        arguments.Add("--profile");
    }

    return arguments;
}

static async Task<ProcessResult> RunProcessAsync(
    string executable,
    IReadOnlyList<string> arguments,
    bool captureOutput)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "dotnet" : executable,
        UseShellExecute = false,
        RedirectStandardOutput = captureOutput,
    };
    if (executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
    {
        startInfo.ArgumentList.Add(executable);
    }

    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Could not start '{executable}'.");
    var standardOutput = captureOutput
        ? process.StandardOutput.ReadToEndAsync()
        : Task.FromResult(string.Empty);
    await process.WaitForExitAsync();
    return new ProcessResult(process.ExitCode, await standardOutput);
}

static IReadOnlyList<TextEdit> ParseFixes(string jsonLines)
{
    var edits = new HashSet<TextEdit>();
    foreach (var line in jsonLines.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (!root.TryGetProperty("fixes", out var fixes))
        {
            continue;
        }

        var diagnosticFile = Path.GetFullPath(root.GetProperty("file").GetString()
            ?? throw new InvalidOperationException("A diagnostic has no file path."));

        foreach (var fix in fixes.EnumerateArray())
        {
            edits.Add(new TextEdit(
                fix.TryGetProperty("file", out var fixFile)
                    ? Path.GetFullPath(fixFile.GetString()
                        ?? throw new InvalidOperationException("A fix has an empty file path."))
                    : diagnosticFile,
                fix.GetProperty("start").GetInt32(),
                fix.GetProperty("length").GetInt32(),
                fix.GetProperty("text").GetString() ?? string.Empty));
        }
    }

    return edits.ToArray();
}

static async Task<int> ApplyFixesAsync(IReadOnlyList<TextEdit> edits)
{
    var applied = 0;
    var groups = edits.GroupBy(static edit => edit.FilePath, GetPathComparer())
        .Select(group => (Path: group.Key, Edits: group.OrderByDescending(static edit => edit.Start).ToArray()))
        .ToArray();
    foreach (var group in groups)
    {
        ValidateEdits(group.Path, group.Edits);
    }

    foreach (var group in groups)
    {
        var source = await File.ReadAllTextAsync(group.Path);
        var builder = new StringBuilder(source);
        foreach (var edit in group.Edits)
        {
            if (edit.Start < 0 || edit.Length < 0 || edit.Start + edit.Length > builder.Length)
            {
                throw new InvalidOperationException(
                    $"A fix for '{group.Path}' has an invalid span ({edit.Start}, {edit.Length}).");
            }

            builder.Remove(edit.Start, edit.Length);
            builder.Insert(edit.Start, edit.NewText);
            applied++;
        }

        var temporaryPath = group.Path + $".drillpress-{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporaryPath, builder.ToString(), new UTF8Encoding(false));
        File.Move(temporaryPath, group.Path, overwrite: true);
    }

    return applied;
}

static void ValidateEdits(string path, IReadOnlyList<TextEdit> edits)
{
    for (var index = 1; index < edits.Count; index++)
    {
        var later = edits[index - 1];
        var earlier = edits[index];
        if (earlier.Start + earlier.Length > later.Start)
        {
            throw new InvalidOperationException(
                $"Fixes overlap in '{path}' at offsets {earlier.Start} and {later.Start}.");
        }
    }
}

static StringComparer GetPathComparer() => OperatingSystem.IsWindows()
    ? StringComparer.OrdinalIgnoreCase
    : StringComparer.Ordinal;

internal sealed record ProcessResult(int ExitCode, string StandardOutput);

internal sealed record TextEdit(string FilePath, int Start, int Length, string NewText);

internal sealed record Options(
    string Command,
    string Rules,
    string Target,
    string Format,
    string? BuildHost,
    bool Fast,
    bool AllowCompilerErrors,
    bool Profile,
    bool Details,
    bool IncludeContexts,
    IReadOnlyList<string> Properties)
{
    public static bool TryParse(string[] arguments, out Options options)
    {
        var command = arguments.FirstOrDefault() ?? string.Empty;
        var rules = string.Empty;
        var target = string.Empty;
        var format = "llm";
        string? buildHost = null;
        var fast = false;
        var allowCompilerErrors = false;
        var profile = false;
        var details = false;
        var includeContexts = false;
        var properties = new List<string>();
        var valid = command is "check" or "fix";

        for (var index = 1; valid && index < arguments.Length; index++)
        {
            switch (arguments[index])
            {
                case "--rules" when index + 1 < arguments.Length:
                    rules = Path.GetFullPath(arguments[++index]);
                    break;
                case "--build-host" when index + 1 < arguments.Length:
                    buildHost = Path.GetFullPath(arguments[++index]);
                    break;
                case "--format" when index + 1 < arguments.Length:
                    format = arguments[++index];
                    break;
                case "--property" when index + 1 < arguments.Length:
                    properties.Add(arguments[++index]);
                    break;
                case "--fast":
                    fast = true;
                    break;
                case "--allow-compiler-errors":
                    allowCompilerErrors = true;
                    break;
                case "--profile":
                    profile = true;
                    break;
                case "--details":
                    details = true;
                    break;
                case "--include-contexts":
                    includeContexts = true;
                    break;
                default:
                    if (arguments[index].StartsWith("--", StringComparison.Ordinal) ||
                        !string.IsNullOrEmpty(target))
                    {
                        valid = false;
                    }
                    else
                    {
                        target = Path.GetFullPath(arguments[index]);
                    }

                    break;
            }
        }

        valid = valid &&
                File.Exists(rules) &&
                !string.IsNullOrWhiteSpace(target) &&
                format is "llm" or "jsonl" or "text" &&
                (!details || format == "jsonl") &&
                properties.All(static property =>
                    property.IndexOf('=') is > 0 and var equals && equals < property.Length - 1);
        options = new Options(
            command,
            rules,
            target,
            format,
            buildHost,
            fast,
            allowCompilerErrors,
            profile,
            details,
            includeContexts,
            properties);
        return valid;
    }

    public static void WriteUsage()
    {
        Console.Error.WriteLine(
            "Usage: drillpress <check|fix> --rules <rule-bundle.dll|native-rule-bundle> " +
            "<solution|project|file|directory|glob> [--build-host <path>] [--format llm|jsonl|text] " +
            "[--details] [--include-contexts] [--property Name=Value] [--fast] " +
            "[--allow-compiler-errors] [--profile]");
    }
}
