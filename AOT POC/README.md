# Drill Press NativeAOT proof of concept

This proof of concept uses one solution-wide rule model for syntax, semantic,
project, and cross-project rules. Rule definitions are ordinary C# 14 code.
They are compiled directly into an executable rule bundle: there is no rule
file parser, expression-tree compilation, reflection-based discovery, or
dynamic managed assembly loading.

All projects target .NET 10, use C# 14 and nullable reference types, and treat
warnings as errors. The rule engine uses Roslyn 5.9.0. The rule bundle and
coordinator support NativeAOT; the SDK-facing BuildHost is deliberately managed
because MSBuild and workspace evaluation are outside the AOT boundary.

## Architecture and execution boundary

```text
                                      ┌─> managed BuildHost ─> temporary snapshot ─┐
solution/project ─> NativeAOT CLI ────┤                                         ├─> compiled rule bundle ─> JSONL
file/glob/manifest ─> NativeAOT CLI ──┴─────────────────────────────────────────┘
```

There are two target-loading paths:

- The direct loader runs inside the rule bundle for individual files, globs,
  and prebuilt manifests. It deliberately implements only a small predictable
  subset of project evaluation and remains useful when calling a bundle
  without the coordinator.
- BuildHost uses `MSBuildWorkspace`, runs source generators, and serializes the
  resulting compilations. The CLI automatically uses this fidelity path for
  solutions, projects, and directories, passes its temporary snapshot to the
  rule bundle, and removes it afterward. The NativeAOT rule bundle only parses
  the data manifest and C# syntax; it never loads MSBuild, evaluates a project,
  discovers rules, or compiles rule text.

The rule DLL is an executable contract rather than a plugin loaded into the CLI
process. It accepts `check|fix`, a target, and an optional output format. This
keeps rule discovery static and avoids runtime assembly loading under AOT.

## Projects

- `DrillPress.Core` contains the composable rule API, Roslyn-backed solution
  model, target loader, deterministic diagnostics, and text-edit application.
- `DrillPress.BuildHost` is the SDK-side compiler front end. It uses evaluated
  MSBuild projects to export a versioned, immutable compilation manifest.
- `DrillPress.SampleRules` defines seven rules and builds to an executable
  `DrillPress.SampleRules.dll`. The same project publishes to a native binary.
- `DrillPress.Cli` is a small NativeAOT coordinator. It orchestrates BuildHost,
  temporary manifests, compiled rule bundles, and fix/re-export/recheck. It
  starts either a managed executable rule DLL through `dotnet`, or a natively
  published rule bundle directly, and never loads rule code into its process.
- `DrillPress.ConformanceTests` is a dependency-free executable test suite for
  loading, manifest reconstruction, JSONL, fixes, and evaluated SDK features.

The sample target is in the repository's separate `Sample Solution` folder.

## Build and run

From the repository root:

```bash
dotnet build "AOT POC/DrillPress.Aot.slnx"

dotnet "AOT POC/src/DrillPress.Cli/bin/Debug/net10.0/drillpress.dll" \
  check \
  --rules "AOT POC/src/DrillPress.SampleRules/bin/Debug/net10.0/DrillPress.SampleRules.dll" \
  --build-host "AOT POC/src/DrillPress.BuildHost/bin/Debug/net10.0/DrillPress.BuildHost.dll" \
  "Sample Solution/DrillPress.SampleTarget.slnx"
```

For a deployed layout, place `DrillPress.BuildHost.dll` and its managed output
next to the coordinator. The CLI resolves BuildHost in this order:
`--build-host`, `DRILLPRESS_BUILD_HOST`, then an adjacent
`DrillPress.BuildHost.dll` or native executable. `--property Name=Value` may be
repeated for MSBuild properties. Validated export is the default;
`--fast` explicitly skips the compiler-diagnostics audit.

JSON Lines is the default output. A compact human format is also available:

```bash
dotnet "AOT POC/src/DrillPress.SampleRules/bin/Debug/net10.0/DrillPress.SampleRules.dll" \
  check "Sample Solution" --format text
```

Supported targets are `.sln`, `.slnx`, `.csproj`, `.cs`, a directory, or a
quoted `*`/`**`/`?` file pattern. A `.drillpress.json` compilation manifest is
also a target.

Exit codes are `0` for clean, `1` for findings, and `2` for invalid input or an
analysis failure. Diagnostics go to stdout; operational messages go to stderr.

Each default JSONL record is independently parseable and contains schema
version `v`, rule id, severity, file, absolute text span, one-based line and
column, remediation message, and optional text edits. For example:

```json
{"v":1,"rule":"DP1004","severity":"warning","file":"src/A.cs","start":42,"length":12,"line":3,"column":16,"message":"Use the empty string literal \"\" instead of string.Empty.","fixes":[{"file":"src/A.cs","start":42,"length":12,"text":"\"\""}]}
```

Machine consumers should use the numeric span for edits and line/column for
display. Findings are ordered deterministically by file, span, and rule id.

## NativeAOT

Publish both executable components for a runtime identifier:

```bash
dotnet publish "AOT POC/src/DrillPress.Cli/DrillPress.Cli.csproj" \
  -c Release -r linux-x64 -o "AOT POC/artifacts/cli-linux-x64"

dotnet publish "AOT POC/src/DrillPress.SampleRules/DrillPress.SampleRules.csproj" \
  -c Release -r linux-x64 -o "AOT POC/artifacts/rules-linux-x64"
```

Then run the native coordinator and native rules:

```bash
"AOT POC/artifacts/cli-linux-x64/drillpress" \
  check \
  --rules "AOT POC/artifacts/rules-linux-x64/DrillPress.SampleRules" \
  --build-host "AOT POC/src/DrillPress.BuildHost/bin/Release/net10.0/DrillPress.BuildHost.dll" \
  "Sample Solution/DrillPress.SampleTarget.slnx"
```

Roslyn 5.9 has several missing trimming annotations in internal pooled-delegate,
UI-culture, assembly-location, and analyzer-loading paths. The rule bundle roots
the two Roslyn compiler assemblies and narrowly suppresses those warnings. This
POC does not invoke Roslyn's analyzer loader. Native publication and execution
against the sample solution are part of the acceptance check.

### Initial Linux x64 baseline

Measured in this workspace with a warm filesystem cache and eight findings:

| Artifact/run | Result |
| --- | ---: |
| Native coordinator (stripped) | 2.1 MB |
| Managed rule output including Roslyn dependencies | 16.8 MB |
| Native rule bundle (stripped, without `.dbg`) | 66.9 MB |
| Managed rule check, three runs | 0.81-0.90 s |
| Native rule check, three runs | 0.04 s each |

These are POC measurements rather than a formal benchmark, but they preserve a
useful size/startup baseline for comparison with managed execution.
The coordinator grew to 2.8 MB after adding JSONL fix orchestration; the rule
bundle is 69.5 MB before excluding its separate debug-symbol file.

## Rule definitions and composition

Selections and conditions are reusable values:

```csharp
var xunitTests = Code.Methods.Where(Methods.HaveAnyAttribute(
    CodeType.Named("Xunit.FactAttribute"),
    CodeType.Named("Xunit.TheoryAttribute")));
var onlyAssertionIsThrows = Methods.HaveOnlyAssertion(
    Assertions.Are(CodeType.Named("Xunit.Assert"), "Throws"));

rules.For(xunitTests)
    .Require(
        id: "DP1001",
        condition: Methods.HaveAtMostEmptyLines(2),
        message: "Remove extra empty lines; keep at most two blank lines inside an xUnit test.",
        at: Methods.EmptyLineAfter(2))
    .Require(
        id: "DP1002",
        condition: Methods.HaveAllAssertionsAfterLastEmptyLine
            .ExceptWhen(onlyAssertionIsThrows),
        message: "Move every assertion after the final blank line in the xUnit test.",
        at: Methods.FirstAssertionBeforeLastEmptyLine);
```

`RuleCondition<T>` supports `And`, `Or`, `Not`, and `ExceptWhen`.
`ExceptWhen` makes the base requirement pass only when its exception condition
matches; exceptions are therefore ordinary reusable, composable conditions
rather than special cases inside the engine. `CodeQuery<T>.Where` can be layered
to create more specialized reusable queries. The complete definitions are in
`src/DrillPress.SampleRules/SampleRuleSet.cs`.

### Strongly typed type references

Type-sensitive helpers have generic overloads, so rule code gets compiler
checking, navigation, and rename support:

```csharp
Members.Are<string>(nameof(string.Empty))
Members.Are<StringComparer>(nameof(StringComparer.Ordinal))
Members.HaveType<DateTime>()

Methods.AreDeclaredOn<MyService>()
Methods.HaveAttribute<ObsoleteAttribute>()
Methods.Return<List<string>>()
Methods.HaveParameter<CancellationToken>()

Interfaces.Are<IMyContract>()
Types.Are<MyService>()
Types.Implement<IMyContract>()
```

`CodeType.Of<T>()` records the metadata name, exact closed generic arguments,
and—except for core-library facade types—the simple assembly name. Matching is
against Roslyn symbols rather than source spelling, so aliases and fully
qualified names behave identically. The identity is cached per closed `T`; this
uses `typeof(T)` metadata but does not scan assemblies or discover rule types.
A named generic identity with no supplied
arguments intentionally matches any construction; `ConstructedWith` can supply
arguments explicitly:

```csharp
var resultOfString = CodeType
    .Named("Acme.Contracts.Result`1", "Acme.Contracts")
    .ConstructedWith(CodeType.Of<string>());
```

#### Referencing types from another project

A rules project may directly reference a stable contracts/API project:

```xml
<ItemGroup>
  <ProjectReference Include="../Acme.Contracts/Acme.Contracts.csproj" />
</ItemGroup>
```

That enables expressions such as:

```csharp
Members.Are<LegacyApi>(nameof(LegacyApi.OldMethod))
Types.Implement<ICommandHandler>()
```

This is fully static and NativeAOT-compatible: the referenced assembly becomes
part of the rule bundle. It is a good fit for a small, stable contract assembly,
but referencing a large application project increases bundle size and couples
rule recompilation to that project. It must also not introduce a project cycle.

When a direct reference is undesirable or impossible, use a named identity:

```csharp
var commandHandler = CodeType.Named(
    "Acme.Contracts.ICommandHandler",
    "Acme.Contracts");

Code.Types.Where(Types.Implement(commandHandler));
```

Omit the assembly argument when the same metadata type name should match in any
assembly. This fallback loses compile-time rename checking but still performs
semantic matching in the target compilation; it is not runtime rule parsing.

The included rules are:

- DP1001: xUnit tests have at most two empty lines.
- DP1002: xUnit assertions occur after the final empty line, except that a sole
  `Assert.Throws` may occur before the second empty line.
- DP1003: an interface does not have exactly one concrete non-test
  implementation across the selected solution.
- DP1004: use `""` rather than `string.Empty` (fixable).
- DP1005: do not pass `StringComparer.Ordinal` (fixable when removing the
  argument still binds to a method).
- DP1006: use an injected `TimeProvider` rather than `DateTime.Now`.
- DP1007: use an asynchronous cancellable delay rather than `Thread.Sleep`.

## Applying fixes

```bash
dotnet "AOT POC/src/DrillPress.Cli/bin/Debug/net10.0/drillpress.dll" \
  fix \
  --rules "AOT POC/src/DrillPress.SampleRules/bin/Debug/net10.0/DrillPress.SampleRules.dll" \
  --build-host "AOT POC/src/DrillPress.BuildHost/bin/Debug/net10.0/DrillPress.BuildHost.dll" \
  path/to/target.sln
```

For SDK-evaluated targets, the coordinator checks a temporary manifest in JSONL
mode, deduplicates identical edits from multi-target projects, checks all edits
for overlap, and applies them from the end of each file through a temporary
file. It then exports a fresh manifest and rechecks it, so stdout contains only
remaining findings. If exporting or recompiling the changed target fails, the
CLI returns exit code 2 rather than reporting stale results. DP1005 uses
speculative semantic binding and offers its removal only when the rewritten
invocation resolves.

Direct file and glob fixes still run inside the rule bundle. A manifest itself
remains immutable and cannot be fixed directly; the coordinator must receive
the original solution/project target so it can regenerate the snapshot.

## Deliberate POC limitations

The AOT process does not host MSBuild. The coordinator starts the managed
BuildHost as a separate process for compiler-faithful solution and project
checks. The explicit two-step form remains available for CI caching,
diagnostics, and benchmarking:

```bash
# Build first when the graph contains source-generator project references.
dotnet build path/to/target.slnx

dotnet "AOT POC/src/DrillPress.BuildHost/bin/Debug/net10.0/DrillPress.BuildHost.dll" \
  export path/to/target.slnx path/to/target.drillpress.json

"AOT POC/artifacts/rules-linux-x64/DrillPress.SampleRules" \
  check path/to/target.drillpress.json
```

The default export performs a full compiler-diagnostics audit. After a
successful build, latency-sensitive callers can explicitly select the fast path:

```bash
dotnet "AOT POC/src/DrillPress.BuildHost/bin/Release/net10.0/DrillPress.BuildHost.dll" \
  export path/to/target.slnx path/to/target.drillpress.json \
  --skip-compiler-diagnostics
```

Fast manifests store `null` rather than `0` for `compilerErrorCount`, making it
impossible to confuse “not evaluated” with “compiler clean”. Both modes capture
the same compilations and rule inputs. Compilation requests are started
concurrently; diagnostics remain sequential because bounded parallel diagnostic
passes increased wall time and nearly doubled CPU consumption on the xUnit
workload.

The manifest contains evaluated C# parse and compilation options, preprocessor
symbols, source-generator output, linked source, additional files, analyzer
configuration (including effective per-tree compiler severity), NuGet/framework
metadata references, and project references.
The NativeAOT process reconstructs Roslyn compilations from that snapshot; it
does not evaluate MSBuild or parse rule definitions.

Manifest schema v3 preserves nullable compiler-audit results. The per-tree
compiler-severity support originally introduced in v2 remains necessary because
severity can vary per syntax tree;
without preserving this, `TreatWarningsAsErrors` can turn an editorconfig-
suppressed warning into an AOT-side compiler error. The xUnit audit found this
case with CS9113. The external conformance mode verifies that every reconstructed
project has the same compiler-error count as its BuildHost compilation.

Manifests contain complete source text and machine-local absolute paths to
metadata references. Treat them as build artifacts that may contain sensitive
source, do not publish them unintentionally, and do not expect them to be
portable to another SDK installation or machine.

BuildHost writes the manifest even when it finds errors, but returns non-zero
for a workspace failure or any compiler error. `--allow-compiler-errors` relaxes
only the compiler-error gate. `--skip-compiler-diagnostics` omits that audit and
records nullable error counts; workspace failures still produce a non-zero exit.
Up to three compiler errors per project are shown on stderr for diagnosis. This
prevents a fast but semantically incomplete run from being mistaken for a valid
analysis.

Compilation manifests are immutable snapshots. `fix` intentionally rejects a
manifest target because re-checking its embedded source after editing the
original file would be stale. Findings in generated source are reported, but
never offer edits to generated output.

The core POC workflow is complete. Production-oriented work that remains is:

- incremental/cached manifests or a persistent BuildHost to reduce repeated
  `MSBuildWorkspace` startup;
- a defined multi-target diagnostic policy before sharing semantic results or
  deduplicating findings across target frameworks;
- preservation of uncommon metadata-reference properties such as aliases and
  embedded interop types;
- application of analyzer-config severity to Drill Press rules;
- an evaluated `IsTestProject` value rather than the current
  name/path/framework-reference heuristic;
- cancellation, process timeouts, packaging, schema migration policy, and
  Windows/macOS plus another-repository acceptance runs.

## Conformance and realistic fixture

```bash
dotnet run --project \
  "AOT POC/tests/DrillPress.ConformanceTests/DrillPress.ConformanceTests.csproj"
```

The realistic fixture covers conditional symbols, a NuGet reference, linked
source, an `AdditionalFiles`-driven incremental generator, `.editorconfig`,
per-tree compiler severity, project references, test-project classification,
and generated-source fix safety. The suite also compares direct and manifest
diagnostics exactly, checks all target modes, validates the JSONL schema,
checks profiling output, exercises automatic CLI/BuildHost orchestration,
applies the three sample fixes both directly and through the faithful manifest
path, regenerates and rechecks, and builds both fixed solutions. An external
manifest can be checked with `-- --manifest path/to/file.drillpress.json`.

## xUnit benchmark

`benchmarks/run-xunit.sh` checks out xUnit at
`6bbefaed1d0a995bc9970800384f9e8a1b9d2331`, including its submodules, exports
the full solution, publishes the NativeAOT rules, and records export and
analysis wall time plus peak RSS. The checkout is ignored by this repository.
The script uses validated export by default. Once that confirms clean
compilations, set `DRILLPRESS_SKIP_COMPILER_DIAGNOSTICS=1` to reproduce the fast
path.

Use the complete clone and initialize submodules. A shallow clone makes
Nerdbank.GitVersioning fail while calculating version height, and omitting the
`assert.xunit` submodule produces thousands of misleading compiler errors. The
script handles both requirements and performs the pinned locked restore.

The pinned xUnit revision currently has locked-package hash failures for
`Microsoft.DotNet.ILCompiler.10.0.11` in two AOT runner projects. BuildHost
therefore returns 1 for the full export, but the resulting manifest contains 94
projects. A validated export and AOT reconstruction confirm zero Roslyn compiler
errors in all 94. The workspace failures remain in the manifest rather than
being hidden.

Measured on Linux x64 with .NET SDK 10.0.111, Roslyn 5.9.0, a warm filesystem
cache, `/usr/bin/time`, and JSONL aggregated through `jq`:

| xUnit workload | Wall time | Peak RSS | Result |
| --- | ---: | ---: | --- |
| Original validated SDK export | 47.15 s | 1,759,992 KB | Sequential compilation acquisition |
| Optimized validated SDK export | 45.93 s | 1,758,004 KB | Concurrent acquisition; full diagnostic audit |
| Optimized fast SDK export, 2 runs | 22.27-22.95 s | 1,104,148-1,105,628 KB | Diagnostic audit explicitly skipped |
| Original full native analysis | 49.46 s | 2,514,784 KB | 5,776 findings, 686 fixable |
| Query-planned full native analysis | 25.14 s | 1,616,556 KB | Same 5,776 findings and 686 fixes |
| Query-planned full managed analysis | 36.60 s | 1,698,584 KB | Byte-identical JSONL to native |
| Clean v1 test-project export | 2.71 s | 176,352 KB | 96 trees, 68 references, 0 errors |
| Clean v1 native analysis, 3 runs | 0.30-0.32 s | 115,712-116,224 KB | 93 findings |
| Clean v1 managed analysis, 3 runs | 2.02-2.15 s | 130,140-130,364 KB | 93 findings |

The full manifest is 47,841,128 bytes and includes every target-framework
project produced by `MSBuildWorkspace`; it is intentionally a scale test rather
than a deduplicated count of physical source files. The clean v1 project is the
smaller correctness/startup comparison.

The rule optimization preserves the high-level definitions. Conditions such
as `Members.Are<string>(nameof(string.Empty))` now contribute a query-planning
hint, so the model scans syntax once for the four requested member names and
only asks Roslyn to bind those candidates. An unfiltered
`Code.MemberReferences` query still discovers every reference, preserving the
general model. The cross-solution interface rule also uses a one-time
interface-to-concrete-type index instead of rescanning every type per
interface. On a same-session pre-change run the old model took 53.18 seconds,
so the 25.14-second result is a 52.7% wall-time reduction; against the original
documented 49.46-second baseline it is 49.2%. Peak RSS fell by 35.7%.

Pass `--profile` to the rule bundle or coordinator for timings on stderr while
keeping JSONL stdout unchanged. The optimized full xUnit run reported:

| Native analysis phase | Time |
| --- | ---: |
| Manifest load and compilation reconstruction | 2.87 s |
| Planned member-reference preparation | 5.74 s |
| DP1001 xUnit method/blank-line check | 2.19 s |
| DP1002 assertion ordering and exception | 13.02 s |
| DP1003 indexed implementation check | 1.17 s |
| Remaining four member rules together | 0.01 s |
| Output | 0.05 s |

DP1002 is now the largest isolated rule cost. Its semantic assertion binding
cannot safely be shared across target-framework compilations without defining
what happens when symbols, conditional code, or references differ by target.
The 5,776 emitted xUnit records contain 1,547 unique
rule/file/span/message signatures because the benchmark intentionally includes
all target-framework projects. This POC keeps those distinct evaluations
rather than silently trading correctness for a lower number; a production
multi-target policy is the next meaningful performance decision.

### NativeAOT versus managed execution

The managed comparison runs the same Release rule assembly, Core code,
manifest, queries, and output path with `dotnet`; only ahead-of-time versus JIT
execution differs. Its JSONL is byte-for-byte identical to the native run. On
the full xUnit snapshot, NativeAOT was 31.3% faster (25.14 versus 36.60 seconds),
used 4.8% less peak memory, and used roughly half the measured user CPU. With
the fastest SDK export measured above, the two-process end-to-end estimates are
about 47.4 seconds native and 58.9 seconds managed. The small clean-project
numbers also show the much larger native startup advantage: 0.30-0.32 seconds
versus 2.02-2.15 seconds.

This is deliberately an execution-mode comparison, not a second architecture.
A managed-only design could host `MSBuildWorkspace` and rule evaluation in one
process and avoid the manifest handoff, but that would also change isolation,
memory lifetime, deployment, and the rule-DLL contract. It is a reasonable
separate experiment only if those tradeoffs are in scope; the current data says
switching this architecture from NativeAOT to JIT by itself is not a speed win.

Phase timing showed why the fast mode matters. On the final validated run,
solution loading took 18.16 seconds, compilation acquisition 3.98 seconds, and
compiler diagnostics 22.87 seconds; documents and serialization took only 0.50
seconds combined. Fast-mode manifests were structurally identical to validated
manifests after normalizing the timestamp, workspace-generated project IDs, and
the intentionally nullable compiler-error count. The remaining roughly
18-second floor is `MSBuildWorkspace` evaluation and would require a different
compiler-front-end strategy—not JSON or source-capture micro-optimization.
