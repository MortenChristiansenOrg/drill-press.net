# Drill Press

Drill Press checks C# projects for coding-convention violations. It is designed
for AI coding assistants: compact results identify the rule, file, and location
to change, without pages of repeated messages.

## Alpha versions

**All `0.0.X` releases are alpha builds. Every release may introduce breaking
changes, even when only `X` increases; backward compatibility is not guaranteed.**
Pin the tool and all Drill Press SDK packages to the same exact version. Before
upgrading, check the release's compatibility notes and rebuild your rule bundles.

## Install packages

The managed tool requires a .NET 10 SDK and bundles its matching BuildHost.
The commands below apply after `0.0.1` is published to nuget.org. Before
publication, follow the [local artifact instructions](docs/DISTRIBUTION.md#build-and-validate-distribution-artifacts).
From your consuming repository:

```sh
dotnet new tool-manifest -o .config
dotnet tool install DrillPress.Cli --version 0.0.1
dotnet tool run drillpress -- --version
```

Commit `.config/dotnet-tools.json`; subsequent checkouts run `dotnet tool restore`.
For a global installation, use `dotnet tool install --global DrillPress.Cli --version 0.0.1`.
The SDK packages are `DrillPress.RuleAuthoring`, `DrillPress.Engine`, and the
optional existing `DrillPress.Testing` kit; `DrillPress.Manifest` is transitive.
Packages use the [MIT license](LICENSE); the intended public feed is nuget.org.
See [package references, compatibility, packing, and publication](docs/DISTRIBUTION.md)
for the minimal consumer project and isolated-feed validation.

## Preview capabilities

Drill Press is an experimental .NET lint-rule engine.
Commands and APIs may change. The sample bundle includes five general preview rules covering
xUnit test structure, interface implementations, empty strings, and ordinal
comparers, plus configured codec examples. Eligible corrections are marked with `+`. Use `fix`
in place of `check` to apply them and report only remaining findings. Read
[the write and recovery policy](docs/FIXING.md) before applying changes.

Start with the [Windows and Linux fresh-checkout guide](docs/TECHNICAL_PREVIEW.md)
for Release builds, managed/native execution, all target shapes, and the
[preview acceptance gates](docs/PREVIEW_ACCEPTANCE.md).

## Install from source

Writing your own conventions? Start with the [rule author’s manual](user-docs/index.html).
Open `user-docs/index.html` in a browser for the searchable, offline HTML guide,
including examples, an API field guide, and light/dark themes. Repository
maintenance and acceptance-gate documentation remains in `docs/`.

Install Git and a .NET 10 SDK, then clone and build the repository. A specific
SDK patch or feature-band build is not required; see [global.json](global.json)
for SDK selection:

```sh
git clone https://github.com/MortenChristiansenOrg/drill-press.net.git
cd drill-press.net
dotnet build DrillPress.slnx
```

The SDK version in `global.json` is a minimum, not an exact pin. `latestMinor`
roll-forward selects the highest installed .NET 10 SDK at or above `10.0.100`,
including newer feature bands and patches, without rolling forward to .NET 11.
CI installs the .NET 10 channel (`10.x`) rather than the minimum build.

Source builds can run the tool directly as shown below. Installed tools locate
their packaged BuildHost automatically.

## Try it

From the repository root, run the included rule against the sample project:

```sh
dotnet restore "Sample Solution/src/WidgetLibrary/WidgetLibrary.csproj"
dotnet src/DrillPress.Cli/bin/Debug/net10.0/DrillPress.Cli.dll check --build-host src/DrillPress.BuildHost/bin/Debug/net10.0/DrillPress.BuildHost.dll --rules samples/DrillPress.SampleRules/bin/Debug/net10.0/DrillPress.SampleRules.dll "Sample Solution/src/WidgetLibrary/WidgetLibrary.csproj"
```

This source-build example selects the project loader with `--build-host`.
Installed tools need only `--rules` and the target; `--build-host` remains an
explicit override for development. Rule bundles must already be built.

Expected output:

```text
DP1003 Remove interfaces with exactly one concrete non-test implementation.
Sample Solution/src/WidgetLibrary/Contracts.cs
  3:18
DP1004 Use the empty string literal "" instead of string.Empty.
Sample Solution/src/WidgetLibrary/Contracts.cs
  +10:29
DP1005 Avoid passing StringComparer.Ordinal.
Sample Solution/src/WidgetLibrary/WidgetService.cs
  7:43
```

The `+10:29` location offers a safe correction on line 10, column 29.
To check your own C# project,
replace the final argument with the path to its `.csproj` file.

A clean check prints nothing and exits with code `0`. Findings produce code
`1`—including the example above. Code `2` means the check could not complete,
such as when a project cannot be loaded. `check` reports violations without
applying fixes.

The CLI captures and validates a versioned internal bundle response before writing
public diagnostics. Direct bundle execution (`<bundle> check <snapshot>`) is an
internal JSON protocol, not another public diagnostic format. Use matching CLI,
BuildHost, and rule-bundle package versions; snapshot format 3 binds responses to a unique
request and preserves individual compilation and document identities. Captured child
stdout is limited to 64 MiB and stderr to 8 MiB; exceeding either limit stops the
child and fails the check before rendering diagnostics.

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
The renderer and fix writer consume the same validated plan. Scope is the
loaded project graph. `fix` applies retained edits and reports only remaining
findings after rechecking; see the [write and recovery workflow](docs/FIXING.md).

Snapshots contain source and machine-local paths. Rule bundles execute trusted
code; protocol validation is not a sandbox. Native verification retains exact
internal response bytes and `public.stdout`; its report records public UTF-8
bytes and a rough token estimate (bytes / 4, rounded up).

BuildHost accepts `.sln`, `.slnx`, `.csproj`, a directory, a C# file, or a single
quoted C# glob. A directory selects its sole top-level solution, otherwise its
sole C# project; ambiguity requires an explicit file. Globs use `*` for zero or
more characters within a path segment, `?` for one, and `**` for recursive
segments (`**/` also matches no directories). Matching C# paths are deduplicated
and ordinal-sorted. Pattern matching is case-sensitive on both platforms;
recursive expansion skips symbolic links. Quote globs to keep the shell from
expanding them.

Pass repeatable `--property Name=Value` options to override MSBuild global
properties; the last assignment wins. `--property TargetFramework=net10.0`
selects a framework; otherwise every evaluated framework context is retained.
The selected target's `global.json` controls SDK discovery, even when invoked
from another directory. SDK targets must already be restored: BuildHost does
not restore or build them. Source-generator assemblies must also be available.
Failures explain when to install an SDK or run `dotnet restore`.

Loose files and globs always use an ad hoc C# 14 library compilation, even inside
a project. Nullable annotations and warnings are enabled, there are no
preprocessor symbols or implicit usings, and references come from the BuildHost
runtime's trusted platform assembly set (captured with byte fingerprints).
MSBuild properties are rejected in loose mode. These defaults are deliberately
independent of nearby project settings.

Fast export permits ordinary compiler errors. `--validate-compilation` explicitly
enumerates compiler errors and rejects an invalid target. Generator exceptions,
missing inputs, incomplete project graphs, and load failures fail both modes.
Semantic selections do not guess unresolved symbols; syntax queries retain erroneous code.
Source project references remain compilation
references, so an erroneous dependency need not emit an assembly. Generated
source participates in binding but is excluded from reportable candidates and
edits. Explicit evaluated `IsTestProject` values take precedence over inference.

Snapshots preserve physical source text, byte fingerprints, encoding/BOM,
per-tree parse settings and compiler severities, compilation options, aliased
metadata references, and the evaluated source graph. Reconstruction rejects
changed or missing external metadata. Snapshots are sensitive, ephemeral files:
the CLI uses a private temporary directory, writes atomically, and removes it
on success, findings, failure, and cancellation. Unix snapshot files are created
with owner-only read/write permissions. On Windows the CLI protects its
temporary directory with inheritable access restricted to the current user
before starting a child process.

Run compiler conformance:

```sh
dotnet build fixtures/CompilerSnapshot/Interop/Interop.csproj -c Release
dotnet build fixtures/CompilerSnapshot/Generator/Generator.csproj -c Release
dotnet restore fixtures/CompilerSnapshot/Selected.slnx
dotnet run --project tools/DrillPress.Conformance -c Release -- \
  fixtures/CompilerSnapshot/Selected.slnx artifacts/conformance/fixture.json
dotnet run --file scripts/XunitConformance.cs -c Release -- \
  ../drillpress-xunit-conformance artifacts/conformance/xunit
```

The xUnit harness pins revision `6bbefaed1d0a995bc9970800384f9e8a1b9d2331`, initializes
its submodules, records SDK and restore output, and compares live/reconstructed
symbol bindings, conversions, declarations, compiler diagnostics, and current
rule responses. Keep its checkout outside this repository to avoid inheriting
our MSBuild files. Existing checkouts must match the pin and have no tracked
changes. Dependency locks are generated separately under each project's `obj`
and copied into the report directory; upstream lockfiles remain unchanged.
See [profiling](docs/PROFILING.md) for phase measurements on stderr during
ordinary CLI checks and fixes with `--profile`.

See [rule authoring](docs/RULE_AUTHORING.md) for reusable queries, the five sample
rules, semantic type identities, and the exact automatic-fix contracts.

The [composable SDK guide](docs/SDK_CAPABILITIES.md) covers custom query roots,
source and operation analysis, shared facts, project relationships, accepted
source baselines, safe edit construction, and the consumer test kit. The
[codec example](samples/CodecExamples/README.md) exercises the expanded API with
independent architecture and source policies. Authoring types now live in
responsibility-specific namespaces such as `DrillPress.Analysis`,
`DrillPress.Semantics`, and `DrillPress.Fixes`.
