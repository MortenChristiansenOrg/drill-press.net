using System.Diagnostics;

if (!TryParseArguments(args, out var command, out var rules, out var forwarded))
{
    Console.Error.WriteLine(
        "Usage: drillpress <check|fix> --rules <rule-bundle.dll|native-rule-bundle> " +
        "<solution|project|file|directory|glob> [--format jsonl|text]");
    return 2;
}

var startInfo = new ProcessStartInfo
{
    FileName = rules.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "dotnet" : rules,
    UseShellExecute = false,
};
if (rules.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
{
    startInfo.ArgumentList.Add(rules);
}

startInfo.ArgumentList.Add(command);
foreach (var argument in forwarded)
{
    startInfo.ArgumentList.Add(argument);
}

using var process = Process.Start(startInfo);
if (process is null)
{
    Console.Error.WriteLine($"drillpress: could not start rule bundle '{rules}'.");
    return 2;
}

await process.WaitForExitAsync();
return process.ExitCode;

static bool TryParseArguments(
    string[] arguments,
    out string command,
    out string rules,
    out List<string> forwarded)
{
    command = arguments.FirstOrDefault() ?? string.Empty;
    rules = string.Empty;
    forwarded = [];
    if (command is not ("check" or "fix"))
    {
        return false;
    }

    for (var index = 1; index < arguments.Length; index++)
    {
        if (arguments[index] == "--rules" && index + 1 < arguments.Length)
        {
            rules = Path.GetFullPath(arguments[++index]);
        }
        else
        {
            forwarded.Add(arguments[index]);
        }
    }

    return !string.IsNullOrWhiteSpace(rules) && forwarded.Count > 0 && File.Exists(rules);
}
