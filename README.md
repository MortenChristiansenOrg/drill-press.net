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

Run the publication and execution gate from either platform with Python 3.10+
(`python` may be the command name on Windows):

```bash
python3 -m unittest discover -s scripts -p "test_*.py"
python3 scripts/native_bundles.py
```

The gate builds Release managed outputs, publishes natively, then sends the
same BuildHost snapshot to both bundles. Clean, alias-qualified DP1004, and
incompatible-snapshot cases assert exact stdout/stderr bytes and exit codes
0/1/2, followed by a complete CLI-to-BuildHost-to-native check. Comparison is
within one platform; Slice 3 will lock the cross-platform output contract.
Logs and byte outputs are retained in a fresh `artifacts/native-<rid>-*`
directory. `--output <new-directory>` selects a fresh report destination.

### Roslyn compatibility boundary

No whole-assembly roots or warning suppressions are used. Roslyn 5.9.0 exposes
six known diagnostics in `CommonCompiler.GetAssemblyLocation`, pooled callback
helpers, and `RoslynLazyInitializer.EnsureInitialized`. The first already
handles an empty assembly location; pooled callbacks use explicit factories.
The current import-tracking constructor path is exercised by the alias case.
The exact reviewed members are documented next to `KNOWN_WARNINGS` in the gate.
IL2091/IL3000 remain visible rather than errors during publication, but the
gate rejects either code from any other member and rejects all other warnings.
Use the gate, not publication alone, to validate changes. Roslyn upgrades and
new engine paths must revisit these exceptions and extend executable parity.
