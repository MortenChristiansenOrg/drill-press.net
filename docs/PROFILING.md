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
