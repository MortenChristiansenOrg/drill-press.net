# Slice 3: contracts, compact output, and aggregation

This draft PR turns the provisional Slice 1 interchange into explicit internal
contracts and locks the single public output format before the engine and rules
grow around it.

## Goal

Define a fail-fast snapshot envelope and a validated bundle-to-CLI result
protocol, then use them to emit deterministic, token-efficient diagnostics.
The engine must preserve compilation-context correctness internally while
showing an LLM each physical problem only once.

## Implementation scope

- Add a snapshot magic identifier and exact integer format marker.
- Define a source-generated JSON contract for the internal bundle response,
  including rule ID, remediation message, physical path, exact span,
  compilation context, and proposed edits.
- Validate snapshot and response envelopes before consuming their payloads.
- Evaluate each project/target context independently.
- Aggregate equivalent rule, file, span, and message findings after evaluation.
- Intersect proposed edits across all contributing contexts.
- Implement the public grouped rule/file/location renderer.
- Add exact-byte golden tests and output-size reporting.

The internal structured response is not a second public output format. The CLI
captures and validates it, then emits only compact diagnostics.

## Acceptance criteria

- [ ] **s3.1** Every snapshot begins with the expected magic identifier and
  exact integer format marker.
- [ ] **s3.2** An incompatible snapshot fails before payload evaluation with a
  concise instruction to use matching components.
- [ ] **s3.3** The internal result protocol carries all exact data required for
  aggregation and safe edit application and is serialized with generated JSON
  metadata.
- [ ] **s3.4** The CLI rejects a malformed, incomplete, or incompatible bundle
  response without applying edits or copying protocol data to public stdout.
- [ ] **s3.5** Equivalent findings from linked or multi-target source collapse
  to one physical location only after every context has been evaluated.
- [ ] **s3.6** An aggregated finding retains an edit only when every
  contributing context proposes the identical file, span, and replacement.
- [ ] **s3.7** Public output groups by rule and file, prints each remediation
  message once, and emits only line plus a non-default column for each location.
- [ ] **s3.8** A leading `+` marks a common-safe automatic edit; no legend,
  severity, schema field, context list, or summary is printed.
- [ ] **s3.9** A clean run writes no diagnostic text to stdout.
- [ ] **s3.10** Public output ordering is deterministic by rule, file, and span
  on Windows and Linux.
- [ ] **s3.11** Golden tests lock exact public and internal bytes for clean,
  single-context, linked-file, multi-target, fixable, and non-fixable cases.
- [ ] **s3.12** Benchmark output records public bytes and estimated tokens so
  changes to the LLM context cost are visible.

## Dependencies

Depends on #1 for the initial process path and #2 for the native execution path
that must remain byte-identical after these contracts are introduced.
