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

## Native rule bundles

The CLI and BuildHost stay managed. The sample bundle has an explicit compiled
entry point, generated JSON metadata, and statically reachable Roslyn APIs; it
does not discover rules or compile rule code at runtime. Normal `dotnet build`
still produces the executable managed DLL for fast development.

Supported native targets are `linux-x64` (Ubuntu 24.04 or compatible glibc) and
`win-x64` (Windows x64). Build each on its own operating system; cross-OS native
publication is not supported. Use the SDK selected by [global.json](global.json).
Linux needs a native compiler/linker and zlib headers (`sudo apt-get install
clang zlib1g-dev` on Ubuntu; the SDK also supports GCC fallback). Windows needs
Visual Studio 2022 or later with **Desktop development with C++**, including the
Windows SDK. See the [.NET NativeAOT prerequisites](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/).

Publish on Linux:

```bash
dotnet publish samples/DrillPress.SampleRules/DrillPress.SampleRules.csproj -c Release -r linux-x64 -o artifacts/native/linux-x64
```

On Windows use `-r win-x64 -o artifacts/native/win-x64`; the executable ends in
`.exe`. Pass the absolute native executable path to the existing CLI's
`--rules` option instead of the managed DLL. The native bundle needs no .NET
runtime installation, but the managed CLI and BuildHost still do.

Run the publication and execution gate from either platform with the repository's
.NET SDK. The file-based C# entry point delegates to testable helper classes;
no additional scripting runtime is required:

```bash
dotnet run --file scripts/NativeBundles.cs -c Release --no-cache
dotnet test --solution DrillPress.slnx -c Release --no-build
```

The gate builds Release managed outputs, publishes natively, then sends the
same BuildHost snapshot to both bundles. Clean, alias-qualified DP1004, and
incompatible-snapshot cases assert exact stdout/stderr bytes and exit codes
0/1/2, followed by a complete CLI-to-BuildHost-to-native check. Comparison is
within one platform; Slice 3 will lock the cross-platform output contract.
Logs and byte outputs are retained in a fresh `artifacts/native-<rid>-*`
directory. `--output <new-directory>` selects a fresh report destination.

### Measurements and continuous validation

Both tooling projects live under `tools/`. The dependency chain is
`scripts/NativeBundles.cs → DrillPress.Benchmarks → DrillPress.BundleVerification`.
The benchmark runner first calls `VerificationSession.CreateAsync` to build,
publish and verify the bundles, then measures them. The verification library
owns those checks and shared process helpers without depending on BenchmarkDotNet;
the runner owns command dispatch and measurements.

Use `--no-cache` so the file-based app rebuilds referenced helper changes instead
of reusing a stale cached copy. This build remains outside all measurements.
The same C# entry point runs BenchmarkDotNet (centrally pinned) after publication
and parity verification. Its Monitoring job uses one benchmark-process launch,
one warmup, and five single-invocation iterations for each of eight cases:
managed/native × startup/clean/violating/invalid. Compilation is outside timing.
Raw timing data and summaries are in `BenchmarkDotNet/results` (JSON, CSV, HTML,
and Markdown); BenchmarkDotNet also retains its execution log. `report.json`
contains SDK, revision, dirty-worktree status, platform, artifact inventories,
and supplemental memory samples. There are no performance thresholds.

Startup is explicitly a **proxy**: process launch through the no-argument usage
response and exit, including static rule construction but no snapshot loading.
Wall time for each clean, violating, and invalid snapshot includes launch,
loading, evaluation, output draining, and exit. Every measured execution must
still match the exact byte and exit-code contract. BenchmarkDotNet controls
ordering and timing; verification runs first and filesystem caches are not
cleared. These are warm-workflow measurements, not cold-boot claims.

Peak memory is the OS child-process high-water mark: Linux GNU `/usr/bin/time`
`%M`, converted from KiB to bytes, or Windows
`GetProcessMemoryInfo.PeakWorkingSetSize`. Each untimed sample uses a fresh,
prebuilt C# worker. On Linux it delegates child launch to GNU time's small native
process: direct `getrusage` from C# can include the managed launcher's inherited
pre-exec memory floor. Install the `time` package if absent. No time fields from
GNU time are collected. BenchmarkDotNet times the bundle directly, without
either memory-measurement launcher.
Its MemoryDiagnoser is not used: harness allocations are not bundle memory.
Artifact bytes count dedicated publish directories without debug symbols
or XML documentation; the managed runtime and native system-library prerequisites
are external and excluded. Compare reports on the same machine and options.

`.github/workflows/native-bundles.yml` uses Blacksmith's x64 runners:
`blacksmith-2vcpu-ubuntu-2404` (`linux-x64`) and
`blacksmith-2vcpu-windows-2025` (`win-x64`). Enable Blacksmith for the repository
before running the workflow; Windows runners are currently in public beta.
See [Blacksmith runner types](https://docs.blacksmith.sh/blacksmith-runners/overview)
and [setup](https://docs.blacksmith.sh/introduction/quickstart).
The jobs are predominantly sequential; start with 2 vCPUs and resize only when
measured runtime or memory pressure justifies it.
The workflow installs the pinned SDK, uses the
runner's Visual C++ tools on Windows and installs Clang/zlib headers and GNU
time on Linux.
Both jobs run these commands (substitute the platform RID in the output path):

```bash
dotnet run --file scripts/NativeBundles.cs -c Release --no-cache -- --output artifacts/ci-linux-x64
dotnet test --solution DrillPress.slnx -c Release --no-build
```

Build and publish commands are the script's first steps. CI uploads logs,
golden-case stdout/stderr, and measurements even on failure. Later slices must
extend these executable cases as protocols and rule behavior grow; a successful
native publish alone is never the parity gate.

### Roslyn compatibility boundary

No whole-assembly roots or warning suppressions are used. Roslyn 5.9.0 exposes
six known diagnostics in `CommonCompiler.GetAssemblyLocation`, pooled callback
helpers, and `RoslynLazyInitializer.EnsureInitialized`. The first already
handles an empty assembly location; pooled callbacks use explicit factories.
The current import-tracking constructor path is exercised by the alias case.
The exact reviewed members are documented in `PublishWarnings.cs` in the gate.
IL2091/IL3000 remain visible rather than errors during publication, but the
gate rejects either code from any other member and rejects all other warnings.
Use the gate, not publication alone, to validate changes. Roslyn upgrades and
new engine paths must revisit these exceptions and extend executable parity.
