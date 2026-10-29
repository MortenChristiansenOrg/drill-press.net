# Joblet full-check performance

Measured on 28–29 October 2026 on Ubuntu under WSL, with .NET SDK 10.0.113,
runtime 10.0.13, 16 logical processors and 19 GiB RAM, against Joblet `aefe5e22`
and Drill Press `f091234` plus the changes released in 0.0.3-preview.24.

## Result with the published packages

This is what Joblet's check script, pre-push checks and CI get. The table times
`drillpress check` on the whole solution with fresh processes and no findings;
rule builds are excluded. "Two cores" pins the check to two CPUs to stand in for
Joblet's 2-vCPU CI runners. Another project's test suite loaded the host
throughout, so the runs are interleaved and each variant runs twice.

| Workflow | All cores | Two cores |
| --- | ---: | ---: |
| 0.0.3-preview.23 (Debug rules locally, Release on two cores, as in CI) | 84.5 / 76.1 s | 98.2 / 99.4 s |
| 0.0.3-preview.24, rules with tiered compilation off and server GC | 42.1 / 39.3 s | 61.2 / 58.0 s |
| + `--parallel-queries` | 30.2 / 29.4 s | 55.6 / 52.6 s |
| + `--stream-compilation`, as Joblet's script now runs it | **24.6 / 24.5 s** | **48.5 / 46.5 s** |
| Same, tiered compilation re-enabled in the rule bundle | — | 81.6 / 68.7 s |
| Same, workstation GC in every process | — | 51.9 / 48.8 s |

On two cores the check takes about half as long as before. The largest
single process grows from 0.90 GB to 1.06–1.21 GB at peak. CI runs this check
after its build and tests, so the saving there is about 50 seconds per run.

Without a source generator catalogue, `--stream-compilation` runs every generator
in BuildHost and still overlaps capture with compilation in the rule bundle.

## Result with a Drill Press source checkout

**With native bundles built from a source checkout, the full default command takes
less than 10 seconds from fresh processes.** Three final default invocations took
**9.80, 9.41 and 9.42 s**, exit zero and emit no findings or stderr. Builds remain
enabled:

```sh
cd joblet
sh scripts/check-code-rules.sh
```

| Final scenario | Complete command | Result |
| --- | ---: | --- |
| Fresh processes, profiling enabled | 9.11 / 9.35 s | 14 contexts, 125 rule phases, zero findings |
| Dataset source edited, fresh processes | **9.16 s** | Same complete scope |
| Core source edited, fresh processes | **9.11 s** | Same complete scope |
| Exact default invocation, no profiling | **9.80 / 9.41 / 9.42 s** | Zero findings; empty stderr |
| Additional profiled default confirmation | 9.44 s | Complete check |

The median of the two final profiled cold checks is **9.23 s**, versus **79.51 s**
for the original published workflow: about **8.6× faster**, or **88% less time**.
The valid earlier source baseline was 51.54 s.

Cold here means new CLI, capture and rule processes with no retained application
input, generated-compilation, semantic-model or query-result cache. The SDK,
restored packages and compiled tool artifacts are present; the OS file cache is
not flushed. Source generation and semantic work are shared *within* each check.
Neither a previous response nor a previous analysis is required.

**Ten seconds is a measured result on this host, not a latency guarantee under
contention.** Other worktrees were running tests and RavenDB processes during
later validation. Retained contended batches took 11.35–20.49 s; isolated exact
default attempts took 19.13 and 21.81 s. These slower runs remain in the evidence.
No other compilation or test job from this task ran during acceptance timings.

## When one project changes

The Dataset and Core timings above each change a source comment and start entirely
fresh analysis processes. Every project and rule still runs. The harness restores
the original bytes and timestamps afterward. The cold speedup therefore applies
to changed input, including a central dependency; it is not restricted to repeating
an unchanged check.

There is also an optional worker path. Earlier valid worker measurements were
32.71 s for the first request, 3.98 s unchanged and 15.22 s after a Dataset edit,
versus 29.45 s for that edited input in fresh processes. The edited worker reused
12 of 14 project generations; Dataset and dependent Core.Tests were invalidated.
That retained about 61% of the warm-run saving. A Core edit has greater fan-out.
These are historical worker measurements, preceding the final native cold path,
not measurements of the final default workflow.

Workers revalidate declared inputs and metadata, invalidate changed contexts and
transitive dependents, and rerun all rules, allowances and fix validation. Coverage
refreshes cannot reuse stale query evidence. Generators with undeclared external
state need fresh execution, which remains the default.

## Overview of retained solutions

These complete-workflow stages combine related changes; they do not assign each
stage's saving to a single implementation detail.

| Complete workflow | Time | Incremental reduction |
| --- | ---: | ---: |
| Original published packages | 79.51 s | — |
| Valid source baseline | 51.54 s | 27.97 s |
| Optimized managed source workflow | 28.63 s | 22.91 s |
| Final shared-compiler/native workflow, profiled median | **9.23 s** | **19.40 s** |

| Solution | Observed impact | Measurement scope |
| --- | --- | --- |
| Reference this checkout's SDK, CLI, capture host and testing library | Establishes the local development workflow | Required setup; no isolated speed attribution |
| Release rules with tiering disabled | 39.60 → 23.40 s | Saved-input managed rule process |
| Reject unrelated call names and owners before resolution checks; share error evidence | Avoids repeated member/span diagnostics in clean projects | Exact document/member fallback remains when errors exist, including promoted warnings |
| Parallel snapshot parsing | 2.60 → about 0.94 s | Isolated reconstruction |
| Concurrent preparation of pure queries | 24.62 → 15.02 s; identical response bytes | Saved-input rules; reporting and fix factories stay ordered |
| In-process MSBuild compiler-command capture | About 19.3 → 15.9–17.1 s | Managed export; unsupported targets retain workspace loading |
| Filter declaration names before symbol binding; add NameMatching(Regex) | G3 0.539 → 0.223 s; full cold 29.99 → 29.58 s | Matched earlier managed workflow; Joblet's predicate becomes shorter |
| Gate unchanged source and rule builds, with membership inventories | Build preparation 1.28 → 0.62 s | Earlier matched build preparation; add/delete/configuration/missing-output probes pass |
| Server GC in compiler processes | Native rules 11.25 → 7.12 s; managed 16.75 → 11.68 s; precompiled export 11.28 → 10.06 s | Process-level comparisons with identical outputs |
| Stream generation and share actual semantic callback models with rules | Managed real CLI 20.05 s; native real CLI 9.24 s | Fresh processes, build checks excluded |
| Publish a native CLI | About 0.4 s saved | Coordinator startup; initial pipeline controls 9.11 / 8.99 s |
| Tune the shared pipeline's GC/thread budget | Sixteen-heap native pipeline trial 8.45 s | Build checks excluded; one overlapping-format trial discarded |
| Reuse already captured source/reference data within the check | Exploratory managed CPU 86.08 → 85.18 s; peak 2.06 → 1.94 GiB | Small, non-isolated comparison; avoids duplicate file reads and preserves captured identity |
| Final integrated source workflow | 9.11 / 9.35 s cold; 9.16 / 9.11 s edited | Complete commands with builds enabled |

Inclusive and overlapping phase measurements cannot be added as independent wall
costs. Final timing differences also include changing host contention; the final
cold improvement is not attributed solely to the last capture change.

## The decisive compiler change

Capture streams projects as their design-time builds finish. A statically linked
source-generator catalogue lets the rule process perform generation and prepare
semantic models once, instead of generating in one process and reconstructing and
binding another complete compilation afterward. Query evaluation consumes the
actual analyzer-callback models through an explicit internal boundary.

The public APIs are `CompiledSourceGenerator`, `SourceGeneratorCatalog` and
`SourceGeneratorCatalogAttribute`. A catalogue identifies exact generator types,
assembly identities and original analyzer SHA-256 fingerprints. The ordinary rule
entry point is unchanged. The source bootstrap generates the catalogue, so Joblet
needs no hand-maintained registry.

Joblet uses two options-generator versions. MSBuild unifies direct references to
both versions, so linking both was rejected. The supported implementation runs in
the bundle; mismatches run in managed capture with actual generated dependency
metadata. Failed reference emission falls back to captured dependency sources.
No version is substituted. In the final snapshot check, two projects use this
managed fallback.

The consumer also reuses immutable source/reference data already captured and
verified during that check. New generated documents and effective compiler options
are still captured, and final filesystem policy still restricts edits. A regression
changes a live source after capture and verifies that analysis retains the original
text and fingerprint. Fix application continues to validate current file identity
and bytes. Each fix recheck uses a distinct input path.

The final profiled cold rule process peaks at about **1.57 GiB**, and capture at
**0.90 GiB**, on this host. These are separate process high-water marks, not a
simultaneous aggregate. The native compiler processes use server GC, up to sixteen
heaps, batch collection and a larger initial thread pool.

## Experiments and rejected approaches

| Attempt | Observation | Decision |
| --- | --- | --- |
| Separate precompiled capture and native rules | Full CLI 25.85 s excluding builds | Shared generation/analysis was still needed |
| Live evaluation inside managed capture | 32.35 s; combined precompilation 30.11 s, or 26.87 s without tiering | Removed |
| Four-gigabyte no-GC budget | Less than one second gained; about 4 GB peak | Removed |
| Early semantic-model sharing and narrow invocation discovery | Small/inconsistent gains or identical elapsed time | Reworked through the explicit callback boundary; no independent gain claimed |
| Document-only diagnostics and eager concurrent guards | Slower or no repeatable gain | Removed |
| Preview operation-root planning before predicates | About 40.66 s versus 15.05 s | Removed; also violated once-per-analysis selector behavior |
| Alternate MSBuild collections/scheduling, isolated SDK precompilation and compiler-version alignment | No useful established gain | Removed; installed SDK and package versions unchanged |
| Early streamed/native-generator prototypes | 23.13 → 12.84 → 10.99 → 9.11 s as the design improved | Diagnostic prototypes only; omitted CLI/build costs and initially incomplete fallback handling |
| Query overlap, metadata identity and source-text transfer variants | 9.06–9.88 s | No independent additional gain established |
| Disable GC affinity, reduce CPU/heap budgets, enable adaptive GC under contention | Full commands roughly 12–19 s | No useful established improvement; final defaults unchanged |
| Disable size-optimized LINQ | 63 → 101 MB executable; roughly fourteen-minute publication; settled pipeline 9.30 versus 9.18 s | Insufficient gain; not retained |
| Analyze semantic callbacks one ordinary document at a time | Functional success, but rule CPU 88.69 versus 86.08 s; elapsed gain followed faster host loading | Not retained |

Several invalid early measurements were corrected: Release SDK outputs were once
combined with incomplete Debug source references and stale rule-test package assets.
Counting projects and phases did not prove complete semantic analysis. Those
82.56 / 7.72 / 47.18 s runs are not acceptance evidence. An unchanged worker result
was also the wrong completion criterion. Overlapping builds and prototype version
substitution were discarded. The final results use verified local references and
version-preserving generation.

## Source wiring and validation

Joblet's ignored `.config/drillpress-source.props` points to this worktree:

```text
/home/morten/.t3/worktrees/drill-press.net/t3-228e2919
```

Both rules and rule tests remove published DrillPress package references and use
`artifacts/local-sdk/`. Release source mode publishes the native CLI, native rule
bundle and composite ReadyToRun capture host through `scripts/SourceCheck.targets`.
Debug remains managed. The source projects stay outside Joblet's analysis scope,
and target capture keeps its original MSBuild configuration.

Artifact refresh after tool/rule/generator-dependency changes takes minutes:
the two final full publications measured 518.76 s and 431.18 s on the busy host.
Application source edits do not require those publications. `--no-build` measures
analysis separately; it was not used for the acceptance results.

Validation completed:

- **552 unit tests** pass after captured-input reuse.
- The full integration suite passes **818 tests**, with one Windows ACL skip;
  the real generated-dependency integration passes again after the final change.
- **91 Joblet rule tests** pass against the local source SDK.
- Complete managed/native parity passes main cases, target forms, complexity
  selection, both safe fixes and recheck, LINQ, occurrence coverage and typed evidence.
- After the final capture change, the native generated snapshot matches ordinary,
  compiler-validated capture exactly: **14 contexts, 122 generated documents,
  zero normalized differences**. A real G3 violation produces byte-identical native
  and ordinary findings.
- Build inventory probes cover source/rule edits, added/deleted inputs, configuration
  switches and missing outputs. Debug skips native publication. Source/published
  restore mode is recorded so old assets cannot conceal a mode switch.
- CSharpier, shell syntax, documentation and whitespace checks are run locally.

Native reference emission explicitly uses portable, metadata-only output without
a PDB stream. Roslyn still retains unused Windows-PDB COM branches during linking.
The native gate permits IL2050/IL2072 only from the exact reviewed upstream origin;
other origins fail. Existing reviewed IL2091/IL3000 warnings remain visible. Source
publication also retains Roslyn's summary warning and the SDK Razor CS8012 warning.

Raw JSON, profiles and validation reports are in ignored
`artifacts/joblet-performance/`. Acceptance data is in `final-cold-checks.json` and
`default-confirmation.json`; contended batches are preserved separately. See
[profiling](PROFILING.md) and Joblet's `docs/code-rules.md` for operation.

## Assumptions and doubts at handoff

No user questions were posed. The remaining qualifications are:

1. Cold measurements assume compiled tool artifacts and restored dependencies.
   Their refresh cost is substantial; the OS file cache is not flushed.
2. Shared-host CPU/memory contention can take checks above ten seconds. The slower
   measurements are retained; this is not a worst-case latency guarantee.
3. Compiler-process memory is higher than the original workstation-GC configuration.
   Other hardware and memory budgets need their own measurement.
4. WSL/Linux x64 is measured. Windows-specific ACL behavior remains skipped here.
5. The regex NameMatching overload is local until released. Returning to the current
   published package requires restoring the old declaration predicate or upgrading
   to a release containing the overload. Keep this worktree available, or update
   the source override when moving it.

No packages were published, messages sent, commits pushed or pull requests merged.
