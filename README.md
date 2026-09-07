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

The CLI captures and validates a versioned internal bundle response before writing
public diagnostics. Direct bundle execution (`<bundle> check <snapshot>`) is an
internal JSON protocol, not another public diagnostic format. Use matching CLI,
BuildHost, and rule-bundle builds; snapshot format 2 binds responses to a unique
request and preserves individual compilation and document identities.

Public output is UTF-8 without a BOM, uses LF, and groups ordinally by rule and
relative file path, then source span. Each rule's remediation appears once. A
location is `line` or `line:column` (one-based physical UTF-16 coordinates); `+`
before the line marks an agreed safe correction. Paths containing controls,
quotes, or surrounding whitespace use JSON escaping. Clean runs emit nothing.

Corrections are represented internally as complete edit batches tied to original
byte fingerprints. All reporting contexts must agree and every affected loaded
context must validate the complete batch, including contexts without a finding.
Missing, inactive-source, or ambiguous-binding validation withholds the fix.
Conflicting batches are withheld in full; independent fixes survive. Insertions
conflict at either boundary of another edit when their order would be ambiguous.
The renderer and future fix writer consume the same validated plan. Scope is the
loaded project graph. Production editable-source capture arrives with target
loading in Slice 4, and automatic writes arrive in Slice 6.

Snapshots contain source and machine-local paths. Rule bundles execute trusted
code; protocol validation is not a sandbox. Native verification retains exact
internal response bytes and `public.stdout`; its report records public UTF-8
bytes and a rough token estimate (bytes / 4, rounded up).
