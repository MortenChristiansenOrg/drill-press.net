# Drill Press

Drill Press is a compiled .NET lint-rule engine whose compact output is designed
for LLM consumption. The initial managed vertical runs one DP1004 rule that
rejects `string.Empty` in favor of `""`.

Build the pinned .NET 10 and C# 14 solution:

```bash
dotnet build DrillPress.slnx
```

Run the managed development path against the sample project:

```bash
dotnet src/DrillPress.Cli/bin/Debug/net10.0/DrillPress.Cli.dll check \
  --build-host src/DrillPress.BuildHost/bin/Debug/net10.0/DrillPress.BuildHost.dll \
  --rules samples/DrillPress.SampleRules/bin/Debug/net10.0/DrillPress.SampleRules.dll \
  "Sample Solution/src/WidgetLibrary/WidgetLibrary.csproj"
```

Diagnostics are written to stdout. A clean run writes nothing. Exit code `0`
means clean, `1` means findings, and `2` means invalid input or tool failure.
BuildHost and the rule bundle run as child processes; their temporary snapshot
is removed before the CLI exits.

Each rule appears once with its ID and message, followed by each project-relative
source path and its indented `line:column` locations. Repeated violations only
add locations beneath that rule instead of repeating its description:

```text
DP1004 Use the empty string literal "" instead of string.Empty.
Sample Solution/src/WidgetLibrary/Contracts.cs
  10:29
```
