# Slice 8: Windows and Linux technical preview

This draft PR validates and documents the complete focused workflow on both
supported platforms. It is the integration gate for declaring the technical
preview ready, not an expansion of product scope.

## Goal

From a fresh checkout on Windows or Linux, a contributor must be able to build
Drill Press, compile and publish the sample rule bundle, check every supported
target shape, apply all common-safe fixes, and understand the remaining compact
diagnostics.

## Implementation scope

- Run Release build, unit, conformance, and integration jobs on Windows and
  Linux.
- Publish and execute the NativeAOT sample rule bundle on both platforms.
- Exercise managed CLI and BuildHost discovery, process arguments, exit codes,
  stdout/stderr separation, temporary snapshots, and atomic source replacement.
- Cover platform path casing, separators, executable naming, encodings, and
  line endings.
- Run representative repository checks and preserve their diagnostics and
  benchmark reports.
- Document the architecture, rule authoring, supported targets, `check`,
  `fix`, compact output, profiling, validation, and trust boundaries.
- Keep CLI help short enough for an LLM or developer to identify the primary
  workflow without reading secondary implementation details.
- Validate core failure behavior for invalid targets, incompatible snapshots,
  malformed bundle responses, conflicting fixes, and stale source.

This slice does not add package distribution, a project template, another
public output format, or additional convention rules.

## Acceptance criteria

- [ ] **s8.1** Release builds and all unit, conformance, and integration tests
  pass on supported Windows and Linux environments.
- [ ] **s8.2** The sample rule bundle publishes and runs with NativeAOT on both
  platforms.
- [ ] **s8.3** Managed and native bundle executions remain byte-identical for
  the platform's golden cases.
- [ ] **s8.4** Solution, project, directory, C# file, and glob targets complete
  the full CLI-to-BuildHost-to-bundle path on both platforms.
- [ ] **s8.5** `check` preserves deterministic compact stdout and documented
  exit codes on both platforms.
- [ ] **s8.6** `fix` preserves encoding and line endings, performs atomic
  replacement, rechecks the target, and leaves the sample solution buildable
  on both platforms.
- [ ] **s8.7** Temporary snapshots are restricted and removed after clean,
  findings, and failure outcomes on both platforms.
- [ ] **s8.8** Invalid targets, incompatible snapshots, malformed bundle
  responses, conflicting edits, and stale source fail safely without partial
  source modification.
- [ ] **s8.9** A fresh-checkout guide covers build, managed execution,
  NativeAOT publication, rule authoring, `check`, and `fix` on Windows and
  Linux.
- [ ] **s8.10** CLI help concisely documents target and bundle selection,
  MSBuild properties, compiler validation, and profiling while prioritizing
  `check` and `fix`.
- [ ] **s8.11** Documentation explains that rule bundles are executable code,
  snapshots contain source and machine-local paths, and fixes modify original
  files.
- [ ] **s8.12** Representative repository reports are retained for both
  platforms without imposing absolute performance or finding-count gates.
- [ ] **s8.13** Every acceptance criterion in #1, #2, #3, #4, #5, #6, and #7
  is complete before the technical preview is declared ready.

## Dependencies

Depends on #1, #2, #3, #4, #5, #6, and #7. This is the final integration and
documentation slice.
