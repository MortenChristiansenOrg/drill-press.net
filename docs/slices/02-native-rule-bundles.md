# Slice 2: NativeAOT rule bundles

This draft PR proves that the rule DLL and Roslyn-backed engine from Slice 1
can be published and executed with NativeAOT on both supported platforms. The
CLI and BuildHost remain managed.

## Goal

Publish the sample rule bundle as native Windows and Linux executables while
preserving the managed rule DLL as the fast development path. For an identical
snapshot, managed and native execution must be behaviorally indistinguishable.

## Implementation scope

- Configure the rule bundle and its engine dependencies for NativeAOT.
- Add Windows and Linux publication commands and CI publish checks.
- Root only the Roslyn code required by the engine and document any narrowly
  justified trimming suppressions.
- Run the managed and native bundles against the same Slice 1 snapshot.
- Compare exit code, stdout, stderr policy, and diagnostic bytes.
- Record artifact size, startup time, peak memory, and wall time as
  measurements rather than release thresholds.

This slice does not make the CLI native, create package distribution, or
broaden the rule model.

## Acceptance criteria

- [ ] **s2.1** The sample rule project still builds as an executable managed
  `.dll`.
- [ ] **s2.2** The sample rule bundle publishes successfully with NativeAOT for
  the supported Windows and Linux runtime identifiers.
- [ ] **s2.3** Published native bundles run without requiring dynamic rule
  discovery or runtime source compilation.
- [ ] **s2.4** Managed and native bundles produce byte-identical diagnostic
  stdout and the same exit code for clean, violating, and invalid snapshots.
- [ ] **s2.5** Native publication introduces no unexplained trim or AOT
  warnings; every suppression is narrow and documented next to its use.
- [ ] **s2.6** Windows and Linux CI jobs build the managed solution and publish
  the native sample bundle.
- [ ] **s2.7** A repeatable script records managed and native artifact size,
  startup, wall time, and peak memory without imposing hardware-specific gates.

## Dependencies

Depends on #1, which establishes the managed engine, snapshot, and executable
rule-bundle path that this slice publishes natively.
