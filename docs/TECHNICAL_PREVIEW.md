# Windows and Linux technical preview

This preview supports `linux-x64` and `win-x64`. Commands and authoring APIs can
still change. Build and run from source; there is no package installation or
project template. Run the commands below from the repository root.

## Fresh checkout

Install Git and the .NET SDK selected by [global.json](../global.json). Native
rule publication also needs the [NativeAOT toolchain](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/):
Visual Studio 2022 or later with **Desktop development with C++** on Windows;
Clang and zlib development headers on Ubuntu. Linux measurements also use GNU
`time`.

On Ubuntu:

```sh
sudo apt-get update
sudo apt-get install -y clang zlib1g-dev time
git clone https://github.com/MortenChristiansenOrg/drill-press.net.git
cd drill-press.net
dotnet build DrillPress.slnx -c Release
dotnet test --solution DrillPress.slnx -c Release --no-build
dotnet publish samples/DrillPress.SampleRules -c Release -p:PublishAot=false --self-contained false -o artifacts/managed
dotnet publish samples/DrillPress.SampleRules -c Release -r linux-x64 -o artifacts/native/linux-x64
```

In Windows PowerShell, after installing the C++ workload:

```powershell
git clone https://github.com/MortenChristiansenOrg/drill-press.net.git
Set-Location drill-press.net
dotnet build DrillPress.slnx -c Release
dotnet test --solution DrillPress.slnx -c Release --no-build
dotnet publish samples/DrillPress.SampleRules -c Release -p:PublishAot=false --self-contained false -o artifacts/managed
dotnet publish samples/DrillPress.SampleRules -c Release -r win-x64 -o artifacts/native/win-x64
```

The CLI and BuildHost stay managed and require .NET; an SDK target also needs its
selected SDK. Only the rule bundle is published natively. Keep the CLI, BuildHost,
and rules from the same build so their snapshot and response versions agree.

## Check and fix

On Linux:

```sh
cli=src/DrillPress.Cli/bin/Release/net10.0/DrillPress.Cli.dll
build_host=src/DrillPress.BuildHost/bin/Release/net10.0/DrillPress.BuildHost.dll
rules=artifacts/managed/DrillPress.SampleRules.dll
target='Sample Solution/DrillPress.SampleTarget.slnx'
dotnet restore "$target"
dotnet "$cli" --help
dotnet "$cli" check --build-host "$build_host" --rules "$rules" "$target"
dotnet "$cli" check --build-host "$build_host" --rules artifacts/native/linux-x64/DrillPress.SampleRules "$target"
dotnet "$cli" fix --build-host "$build_host" --rules "$rules" "$target"
dotnet build "$target" -c Release --no-restore
```

In PowerShell:

```powershell
$cli = 'src/DrillPress.Cli/bin/Release/net10.0/DrillPress.Cli.dll'
$buildHost = 'src/DrillPress.BuildHost/bin/Release/net10.0/DrillPress.BuildHost.dll'
$rules = 'artifacts/managed/DrillPress.SampleRules.dll'
$target = 'Sample Solution/DrillPress.SampleTarget.slnx'
dotnet restore $target
dotnet $cli --help
dotnet $cli check --build-host $buildHost --rules $rules $target
dotnet $cli check --build-host $buildHost --rules artifacts/native/win-x64/DrillPress.SampleRules.exe $target
dotnet $cli fix --build-host $buildHost --rules $rules $target
dotnet build $target -c Release --no-restore
```

The sample deliberately has findings, so a successful `check` exits **1**.
The `fix` commands above modify the original sample files. They apply eligible
string/comparer corrections and can still exit 1 for findings needing manual
changes. Both commands use **0** for clean output and **2** for operational
failure. Help exits 0 without loading a target.

Output groups a remediation once per rule, then relative paths and physical
one-based `line:column` locations. Columns count UTF-16 code units; a default
column of 1 is omitted. A `+` before the line marks a retained common-safe fix.
Paths needing escaping use JSON string escaping. Output is deterministic UTF-8
without a BOM, with LF line endings; a clean result prints nothing. Operational
errors and opt-in profiling go to stderr. Direct bundle JSON is an internal
transport, not an additional public output format.

## Targets and options

Replace the final target argument in either shell's command:

| Target | Example | Loading behavior |
| --- | --- | --- |
| Solution | `Sample Solution/DrillPress.SampleTarget.slnx` or a `.sln` | Loads the selected solution and its source dependency graph. |
| Project | `Sample Solution/src/WidgetLibrary/WidgetLibrary.csproj` | Retains every evaluated framework unless a property selects one. |
| Directory | `Sample Solution` | Selects its sole top-level solution, otherwise its sole C# project; ambiguity fails. |
| C# file | `Sample Solution/src/WidgetLibrary/Contracts.cs` | Uses the documented loose-source compilation. |
| Quoted glob | `'Sample Solution/**/*.cs'` | Uses loose mode, even inside projects; zero matches fail. |

Quote paths with spaces and globs in both shells. Glob matching is case-sensitive
on both platforms: `*` and `?` stay within a segment, while `**` is recursive.
Results are deduplicated and ordinal-sorted; recursive expansion skips links.
Generated/intermediate source is excluded from rule candidates and fixes.

SDK targets must already be restored, and source-generator assemblies must be
built. BuildHost never restores or builds a target implicitly. The target's
`global.json` selects its SDK even when the CLI runs elsewhere. Repeat
`--property Name=Value` to forward MSBuild global properties; the last assignment
wins. For example, `--property TargetFramework=net10.0` selects one framework.

Loose files/globs use a C# 14 library compilation with nullable enabled, no
implicit usings or preprocessor symbols, and captured BuildHost runtime platform
references. Nearby project settings do not affect it, and MSBuild properties
are rejected in loose mode.

Fast loading permits ordinary compiler errors while preserving resolved
semantics. `--validate-compilation` explicitly rejects compiler errors. Missing
SDKs, restore assets, generators, and incomplete graphs fail both modes.
`--profile` emits phase measurements on stderr. `--no-optimization` selects the
exhaustive reference queries for comparison; see [profiling](PROFILING.md).

## Architecture and rule authoring

The managed CLI coordinates two child processes. BuildHost alone hosts SDK and
MSBuild loading and writes a versioned compilation snapshot. The compiled rule
bundle reconstructs compiler semantics, evaluates its explicitly registered
rules, and returns a request-associated response. The CLI validates every context,
aggregates physical locations, filters complete conflicting fix batches, and
renders compact output or applies the common-safe plan. It loads neither Roslyn,
MSBuild, nor rule assemblies into its own process.

Define rules in ordinary C# through `RuleSet`, reusable `Code` queries, and
composable conditions. For example, the sample registers its empty-string rule
with `Code.MemberReferences.Where(Members.Are<string>(nameof(string.Empty)))`
and supplies `EmptyStringFix.Create` to `Forbid`. Its explicit entry point calls
`new RuleApplication().RunAsync(SampleRuleSet.Create(), args)`; there is no reflection
discovery or runtime source compilation. Follow the complete examples and safety
contracts in [rule authoring](RULE_AUTHORING.md), then rebuild and republish the
bundle with the commands above. The bundle includes five general preview rules
and the configured [codec SDK showcase](SDK_CAPABILITIES.md).

## Write policy and trust

Rule bundles are executable code. Run bundles you trust; response validation
does not sandbox them. Snapshots contain source text and machine-local paths.
The CLI restricts its temporary directory before starting a child: Linux uses
mode 700 with snapshot files at 600; Windows protects the directory DACL with
inheritable access for the current user. Snapshots are removed after clean,
findings, failure, and cancellation outcomes. Protect any snapshots or reports
you export and retain yourself; do not publish private-source reports as CI
artifacts.

Fix safety covers the **loaded project graph**. Every affected loaded context
must validate a complete batch, including contexts without a diagnostic.
Conflicting batches are withheld, while independent safe batches proceed.
Malformed responses, invalid targets, incompatible snapshots, ineligible files,
and preflight stale-source failures leave originals untouched.

Fixing is **single pass**, with **per-file atomic replacement**. Encoding, BOM,
and unchanged line endings are preserved. It is not a multi-file transaction:
a late replacement failure retains earlier writes, stops further writes, reports
changed/failed/pending paths, and exits 2. A failed regeneration/recheck retains
writes, reports verification failure, exits 2, and emits no stale diagnostics.
Stop competing writers, inspect the reported files, resolve the failure, then
rerun `check` or `fix`. See the full [fix and recovery policy](FIXING.md).

## Reproduce the acceptance gates

The continuous [native bundle workflow](../.github/workflows/native-bundles.yml)
builds Release, runs all unit/integration tests, publishes and executes both
bundle modes, compares exact bytes for all target shapes, applies real fixes,
and retains compiler-fixture conformance on Windows and Linux. Run its command
locally with a new output directory:

```sh
dotnet run --file scripts/NativeBundles.cs -c Release --no-cache -- --output artifacts/preview-native
```

The additional [repository workflow](../.github/workflows/repository-preview.yml)
uses the same pinned xUnit harness as the compiler and performance slices. These
single-line commands work in either shell; keep the checkout outside this repo
and choose new report directories:

```sh
dotnet run --file scripts/XunitConformance.cs -c Release --no-cache -- ../drillpress-xunit artifacts/preview-conformance
dotnet run --file scripts/XunitConformance.cs -c Release --no-cache -- ../drillpress-xunit artifacts/preview-performance --performance 1
```

CI retains both platforms' raw diagnostics, snapshots, profiles, dependency
locks, identities, and conformance/performance reports as `repository-preview-*`
artifacts for 90 days. This workflow accepts no custom target or repository
inputs: its snapshots come only from this public repository's sample/compiler
fixtures and the fixed public xUnit revision, including their disposable copies.
Those public-fixture snapshots are retained intentionally so compiler inputs can
be inspected alongside the comparison results. This upload policy does not apply
to reports from private targets run outside the workflow.

Download a run's artifacts from
[repository workflow runs](https://github.com/MortenChristiansenOrg/drill-press.net/actions/workflows/repository-preview.yml)
for longer retention. Measurements compare complete signatures across managed,
native, exhaustive, optimized, and disposable fix/recheck runs on the same
machine; absolute timings and finding counts are not release thresholds.

[Acceptance evidence](PREVIEW_ACCEPTANCE.md) maps every implementation slice to
its tests and retained reports. The preview gate requires both platform jobs and
all earlier slice criteria to pass.
