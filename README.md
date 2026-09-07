# Drill Press

Drill Press checks C# projects for coding-convention violations. It is designed
for AI coding assistants: compact results identify the rule, file, and location
to change, without pages of repeated messages.

## Early development

Drill Press is an experimental .NET lint-rule engine, not yet a finished tool.
Commands and APIs may change. The current sample includes one rule: prefer the
empty string literal `""` over `string.Empty`. Automatic fixes and a broader rule
set are not available yet.

## Install from source

Install Git and the .NET SDK specified in [global.json](global.json), then clone
and build the repository:

```sh
git clone https://github.com/MortenChristiansenOrg/drill-press.net.git
cd drill-press.net
dotnet build DrillPress.slnx
```

For now, run the built tool directly from this checkout.

## Try it

From the repository root, run the included rule against the sample project:

```sh
dotnet src/DrillPress.Cli/bin/Debug/net10.0/DrillPress.Cli.dll check --build-host src/DrillPress.BuildHost/bin/Debug/net10.0/DrillPress.BuildHost.dll --rules samples/DrillPress.SampleRules/bin/Debug/net10.0/DrillPress.SampleRules.dll "Sample Solution/src/WidgetLibrary/WidgetLibrary.csproj"
```

The current command requires explicit paths to the project loader
(`--build-host`) and the compiled rules (`--rules`). The example uses both
from the build above.

Expected output:

```text
DP1004 Use the empty string literal "" instead of string.Empty.
Sample Solution/src/WidgetLibrary/Contracts.cs
  10:29
```

This points to a violation on line 10, column 29. To check your own C# project,
replace the final argument with the path to its `.csproj` file.

A clean check prints nothing and exits with code `0`. Findings produce code
`1`—including the example above. Code `2` means the check could not complete,
such as when a project cannot be loaded. The command reports violations; it does
not apply fixes.
