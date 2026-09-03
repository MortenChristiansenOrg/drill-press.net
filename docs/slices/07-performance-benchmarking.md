# Slice 7: measured performance optimization

This draft PR establishes the real-repository benchmark and optimizes only
hotspots demonstrated by profiling. Correct diagnostic and fix signatures are
the non-negotiable comparison baseline.

## Goal

Run the complete check workflow against the pinned xUnit repository, attribute
time and memory to pipeline phases, and implement safe candidate-selection or
indexing improvements where measurements justify them.

## Implementation scope

- Make the pinned xUnit checkout, restore, BuildHost export, managed rule run,
  and native rule run reproducible.
- Add opt-in profiling for target loading, snapshot serialization and loading,
  model preparation, each rule, fix preparation, rendering, and total time.
- Record wall time, user/system CPU, peak RSS, snapshot size, context findings,
  actionable locations, common-safe fixes, and public output size.
- Capture complete diagnostic and fix signatures before changing query
  execution.
- Implement shared syntax candidate preparation and member-name constraints
  when they reduce measured semantic binding.
- Build the interface-to-concrete-implementation index once per analysis if
  DP1003 profiling justifies it.
- Compare managed and NativeAOT rule evaluation using the same snapshot.
- Preserve raw output and measurement metadata for same-machine comparison.

This slice does not introduce snapshot caching, a persistent BuildHost,
hardware-independent timing gates, or fixed expected finding counts.

## Acceptance criteria

- [ ] **s7.1** One documented command prepares the complete pinned xUnit
  repository, including required submodules, and records upstream restore or
  workspace failures rather than hiding them.
- [ ] **s7.2** Benchmark output records SDK and repository identity plus wall
  time, CPU, peak RSS, snapshot size, finding counts, safe-fix counts, and
  public output bytes.
- [ ] **s7.3** Profiling reports BuildHost, reconstruction, preparation,
  per-rule, fix, rendering, and total phases on stderr without changing public
  diagnostic stdout.
- [ ] **s7.4** Pre-optimization diagnostic and common-fix signatures are stored
  for exact comparison.
- [ ] **s7.5** Member-name conditions can contribute safe candidate constraints
  so irrelevant identifiers are not semantically bound.
- [ ] **s7.6** Unfiltered member-reference queries still discover every
  reference, and `And`, `Or`, `Not`, and exceptions preserve complete
  behavior.
- [ ] **s7.7** Cross-project implementation lookup does not rescan every type
  for every interface after its index is prepared.
- [ ] **s7.8** Optimized and unoptimized runs produce identical aggregated
  diagnostic and common-fix signatures across sample, conformance, and xUnit
  targets.
- [ ] **s7.9** Reports retain raw measurements and phase timings suitable for
  before-and-after comparison on the same machine.
- [ ] **s7.10** The benchmark reports managed/native rule execution and compact
  output size without treating absolute time or exact finding count as a
  release contract.

## Dependencies

Depends on #4 for the real-project snapshot, #5 for the representative rule
workload, and #6 for final diagnostic and common-fix signatures.
