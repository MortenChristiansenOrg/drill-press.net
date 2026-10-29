# Profiling

Add `--profile` to `drillpress check` or `drillpress fix`. It forwards profiling
to BuildHost and the rule bundle and leaves compact diagnostic stdout unchanged.
Direct BuildHost export and bundle check commands also accept `--profile`.
The bundle and CLI accept `--no-optimization` to select exhaustive reference
execution for comparisons.

Each stderr line starts with `drillpress-profile ` followed by one JSON event.
Events identify the process role, PID, phase, wall milliseconds, user/kernel CPU
milliseconds, and process peak working set. Counter events have a non-null
`count` and zero durations. Counters include context findings, aggregated
locations, common-safe batches/edits, member bindings, interface-definition
comparisons, prepared/changed files, snapshot bytes, and public UTF-8 bytes.

Phases include BuildHost loading (with its `loading.workspace` and `loading.contexts`
parts) and snapshot serialization, snapshot loading,
reconstruction, model preparation, every rule, allowances, fix validation and preparation,
commit, aggregation, rendering, response serialization, and process totals.
Candidate collections are lazy: their first rule pays their preparation cost.
Per-rule measurements include that rule's candidate selection and proposed-fix
construction; cross-context fix validation has its own phase.

SDK exports also report `loading.generators.<project>` and
`loading.capture.<project>`. Generation includes preparing reference assemblies;
capture includes optional compiler validation and snapshot construction. These
per-project scopes overlap when contexts run concurrently.

Each `rule.<id>` phase is followed by `rule.<id>.own`, the same scope without
the shared work the rule happened to pay for. Shared work is cached work that
later rules reuse: binding a document's operations or object creations
(`operations`), binding member and type reference candidates
(`member-references`, `type-references`), discovering methods, types and using
aliases (`declarations`), the project graph and call relationships
(`relationships`), `AnalysisFact` and `ProjectFact` values (`facts`),
`MethodFlow.For` graphs and data flow (`flow`), and per-member compiler error
checks (`errors`). After the rules, one `shared.<kind>` phase per kind that did
work reports its total, in name order. So the first rule to read `Code.Calls`
shows a large `rule.<id>` but an ordinary `rule.<id>.own`, and
`shared.operations` shows what binding cost everyone. A rule's own work also
includes materializing its own queries, which another rule reusing the same
query instance gets for free. Shared totals include work done while coverage
was planned, before the rules ran.

Only the outermost shared work in a flow counts, including work it runs on other
threads, so shared kinds never count the same time twice. Their CPU is the whole
process's, like every other phase. Measuring each piece of shared work adds a
small cost to profiled runs only.

Measurements are inclusive scopes, not an additive breakdown. CLI child-process
wall phases overlap the corresponding child's own phases. User/kernel CPU and
peak working set describe the reporting process, excluding child processes and
persistent MSBuild servers. Peak working set is the process lifetime high-water
mark at the end of a phase, not a peak attributed solely to that phase. Use raw
per-process measurements for comparisons and retain the process boundaries.

Routine execution without `--profile` emits no timing or progress text. Timings
and finding counts depend on workload, runtime, caches, and machine; they are
not release thresholds.

Profiling is best effort: a failed process probe or output sink disables further
measurements without interrupting diagnostics, source replacement, or cleanup.
Library callers can inspect `PipelineProfile.Failure`. Measurement harnesses must
reject incomplete profiles rather than treating missing phases as zero cost.

## Sharing generation and analysis

`--stream-compilation` starts the rule bundle alongside compiler capture. Projects
stream to the bundle as their design-time builds finish. The bundle compiles them,
runs the generators it links and prepares semantic models once for subsequent rules.
Any bundle supports the option: without a catalogue, BuildHost runs every generator.
Ahead-of-time publication with a catalogue removes the remaining JIT and generator
round trips. Ordinary rule registration and the program entry point stay unchanged.

A bundle declares linked implementations through `SourceGeneratorCatalog` and an
assembly-level `SourceGeneratorCatalogAttribute`. Each `CompiledSourceGenerator`
pairs an implementation with the SHA-256 fingerprint of its original analyzer DLL.
Assembly identity, type name and fingerprint must all match. Missing implementations,
different versions and generators that cannot be statically constructed run in
BuildHost, receiving the actual generated dependency metadata. Failed reference
emission uses the dependency's captured sources instead. No generator version is
substituted to make a native catalogue fit.

The source-check bootstrap in `scripts/SourceCheck.targets` discovers generators,
generates the catalogue and publishes a native CLI, native rule bundle and a
composite ReadyToRun BuildHost. Consumers supply the source checkout, consumer directory,
rule and rule-test project paths, and analysis target as the `DrillPressSource`,
`DrillPressConsumerDirectory`, `DrillPressRulesProject`,
`DrillPressRuleTestsProject` and `DrillPressAnalysisTarget` MSBuild properties.
Bootstrap and tool/rule changes can take minutes to compile. Unchanged build
inputs skip publication; application source edits still get a complete fresh check.

The compiler processes use server GC, up to sixteen heaps, batch collection and a
larger initial thread pool to support parallel compiler work. This trades memory
for latency; measure peak working set alongside time. Roslyn's IL3000/IL2104
warnings and the SDK Razor generator's CS8012 architecture warning remain visible
during publication. The native verification gate also reviews the exact Roslyn
Windows-PDB origins of IL2050/IL2072: metadata-only reference emission requests
Portable PDB format and supplies no PDB stream. Warnings from other origins still
fail that gate. Native parity is required when changing compatibility.

Worker requests and `--validate-compilation` use the ordinary capture path.
Generator transport files live inside the CLI's private temporary directory and
are removed with that check. Fresh-process results require no persisted analysis
or generator-result cache. See [the Joblet measurements](JOBLET_PERFORMANCE.md)
for full-command scope, edited inputs and setup costs.

## Persistent workers

`check`, `fix`, and `status` accept `--worker`. The CLI uses private, current-user
named pipes to reuse BuildHost and rule processes for the target. Workers expire
after five idle minutes; stop the current pair explicitly with:

```sh
drillpress stop-workers --rules path/to/Rules.dll path/to/Target.slnx
```

Each request gets a new snapshot request ID and evaluates every selected rule,
allowance, and fix-validation step. No diagnostic response is cached. BuildHost
reuses an SDK capture only while content fingerprints of sources, references,
evaluated build inputs and imports match and directory inventories are unchanged.
Symlinked directory inventories prevent capture reuse. Loose-source targets are
loaded again on each request. The rule worker revalidates metadata and retains
unchanged compilations, semantic models, and pure query results. Coverage rules
receive a new query evaluation so refreshed evidence cannot reuse old conditions.

Workers assume deterministic generators and declared build inputs. Custom targets
or generators that read undeclared external state need fresh processes: omit `--worker`. A changed
component or process environment selects a new worker; request-specific Codex
tool-call identifiers do not affect worker identity. Cancellation disconnects the
request and retires its worker rather than preserving interrupted analysis.

Report cold, repeated, and edited-input timings separately. Input changes re-evaluate the graph. Unchanged compiler inputs retain their
generated compilations, and reconstruction invalidates a changed context and its
transitive dependents. Solution-wide query materializations are rebuilt after a
graph change; unchanged projects can retain their semantic models. A repeated profile reports work done in that
request; candidate counters and peak working set describe retained worker state.
Omit `--worker` for a fresh-process comparison, and keep source/rule builds
outside analysis-only measurements. Ordinary checks remain fresh by default.

## BuildHost loading

`loading.workspace` captures compiler command lines through an in-process MSBuild
graph for supported single-framework C# targets. Other targets use MSBuildWorkspace
and its separate design-time build host. `loading.contexts`
then evaluates, compiles, runs source generators for and captures every context. A
context starts generating as soon as the contexts it references have generated, so
independent contexts run in parallel and the longest chain of generator runs along the
project graph bounds the phase; validation and capture do not delay dependents. All
contexts share one MSBuild project collection, so SDK imports are parsed once, and each
external reference is fingerprinted once per export. Parallel phases can use more CPU
than wall time.

When several contexts fail, BuildHost reports the first in snapshot order (project path,
then name) whose references loaded, regardless of which failed first.

Measure loading of a synthetic layered solution whose contexts run the SDK's regex and
JSON generators, using the Release BuildHost of the checkout:

```sh
dotnet build DrillPress.slnx -c Release
dotnet run --project tools/DrillPress.Benchmarks -c Release -- --build-host --output artifacts/buildhost-benchmarks
```

## Query optimization

Member-name constraints propagate through conjunctions and bounded alternatives.
Negation and an unrestricted alternative require complete discovery. Bindings are
cached per source context/expression, so filtered queries cannot truncate a later
unfiltered query. Interface lookups index concrete source definitions once per
needed context, preserving compatible framework views and symbol identities.
`--no-optimization` retains the original exhaustive binding and type-scan paths.

Call queries narrowed by `To`, `ToMethodsNamed` or `ToMethodsMatching` bind only
documents whose syntax can produce a matching target name. That set holds every
written identifier plus the names of calls the compiler implies: `.ctor` for
constructor initializers, `Invoke` for delegate calls, `Add` for collection
initializers, LINQ query clause methods, and `AppendLiteral`/`AppendFormatted`
for interpolated string handlers. Each document binds once for all rules and
queries, and documents bind in parallel while candidates keep their order.
Name and target checks run before `IsResolved`, whose compiler-error check
compiles each enclosing member once and queries the exact span only when that
member has errors. Names carry through `Where` and `ExceptWhen` in any order, but
a call query filtered only by a custom `Where`, or reshaped by `Select`,
`SelectMany`, `Union` or `Concat` before naming its target, binds every document.

Place filters (`InProject`, `InTestProjects`, `InNonTestProjects`, `InProjectsWithType`,
`InFolder` and `InFilesNamed`) carry through `Where` and `ExceptWhen` like names: call,
operation, member-reference, type-reference and object-creation roots skip other
documents before binding them, and member and type references outside the analysis
target are never bound. `ObjectCreations.Of(type)` skips explicit creations whose
written type name is neither the type's name nor a using alias; target-typed and
keyword creations are always bound. `MemberReferences.To(members)` binds only the
members' names. `InNamespace` needs a bound location and does not narrow discovery.
Counters `member.symbol.bindings`, `type.symbol.bindings` and
`operation.bound.documents` show how much each root bound.

Declaration `Named` and `NameMatching` constraints reach the declaration root.
`NameMatching` also accepts a `Regex`, for example
`Code.Declarations.NameMatching(new Regex("[æøåÆØÅ]"))`. Known written identifiers
can be rejected before requesting symbols; compiler-generated names and qualified
explicit-interface names retain semantic binding. The ordinary predicate still
checks each resulting symbol name. `--no-optimization` keeps exhaustive discovery.

Path, project, folder, namespace and name globs compile once per distinct pattern.

Measure query scaling with rule count over a synthetic solution:

```sh
dotnet run --project tools/DrillPress.Benchmarks -c Release -- --queries --output artifacts/query-benchmarks
```

Syntax roots and file-scoped `Nodes<T>()` also narrow by project/file/folder before
enumeration. Document-local syntax inventories cache roots and structured trivia; they
are independent of a query's filters and retain original traversal order.
