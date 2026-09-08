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

Phases include BuildHost loading and snapshot serialization, snapshot loading,
reconstruction, model preparation, every rule, fix validation and preparation,
commit, aggregation, rendering, response serialization, and process totals.
Candidate collections are lazy: their first rule pays their preparation cost.
Per-rule measurements include that rule's candidate selection and proposed-fix
construction; cross-context fix validation has its own phase.

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

## Reproduce the pinned repository comparison

From the repository root, with .NET 10 and the NativeAOT prerequisites installed:

```sh
dotnet run --file scripts/XunitConformance.cs -c Release --no-cache -- ../drillpress-xunit artifacts/performance --performance 2
```

Use a new report directory. The command reuses `PinnedXunit`'s single revision,
recursive submodule initialization, SDK selection and isolated restore locks.
It preserves upstream restore/workspace failures and exits unsuccessfully when
any comparison fails. Linux additionally requires `/usr/bin/time` (GNU time).
Windows uses the exited process's CPU counters and peak working set.

The command builds and publishes the managed and native bundles, then measures
the sample solution, compiler conformance fixture and complete pinned xUnit
solution. Each repetition exports a snapshot and executes all four combinations
of managed/native and exhaustive/optimized evaluation against those exact bytes.
It also measures the complete CLI check and verifies its public output.

Every xUnit variant/repetition receives a separate full disposable repository
copy, including Git and submodule metadata. After restoring that copy, the
harness verifies complete compiler inputs, source bytes, membership and
editability against the baseline before measuring CLI fix and its recheck.
It then exports and evaluates the changed copy to preserve complete rechecked
signatures. Copies are deleted on success or failure; the shared checkout is
never fixed. Linked/reparse entries are rejected rather than copied with changed
semantics; other eligibility differences also fail the input comparison.

Reports include environment/SDK/revision identity, tool source hashes and local
changes, bundle hashes, repetition
count, exact commands/options, snapshot sizes, full responses, conflict-filtered
plans, compact public output, counts, raw stdout/stderr, process resources and
phase events. `report.json` records completion or failure; `operations.json` and
`runs.json` preserve completed work if a later operation fails. Raw response logs
remain unmodified. Comparison signatures normalize only request/context/document/
file/batch identities, generated URI context identities, and paths within each
workload root; source text, messages,
replacements, fingerprints and every affected-context safety decision remain
intact. Retained batches are ordered by their normalized identity because their
original order depends on run-specific batch IDs. Compiler option dictionaries
are compared with ordinally sorted keys; their insertion order is not a compiler
input.

Runs are serial, with a fresh process per invocation. Repetitions distinguish
first and repeated observations after restore/publish; filesystem caches are
**not** flushed and the first observation is not a cold-filesystem measurement.
GNU time's user/system CPU includes waited descendants; Windows resource counters
cover the measured process alone. Component profile events provide process-only
CPU on both platforms. Peak RSS/working set is a lifetime high-water mark.
Persistent build servers are outside these measurements. Preserve these scopes
when comparing results on the same machine; do not sum inclusive phase timings.

Member-name constraints propagate through conjunctions and bounded alternatives.
Negation and an unrestricted alternative require complete discovery. Bindings are
cached per source context/expression, so filtered queries cannot truncate a later
unfiltered query. Interface lookups index concrete source definitions once per
needed context, preserving compatible framework views and symbol identities.
`--no-optimization` retains the original exhaustive binding and type-scan paths.
