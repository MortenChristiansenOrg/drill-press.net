# Slice 6: safe fix application and recheck

This draft PR completes the automatic-correction workflow. The CLI—not the
LLM—must apply every edit the engine can prove safe, while leaving ambiguous
violations for the LLM.

## Goal

Implement `drillpress fix` as a guarded multi-file operation:

```text
analyze -> validate common-safe edit plan -> write -> rebuild snapshot -> recheck
```

No original file may be modified until the complete edit plan has passed
validation.

## Implementation scope

- Request and validate the structured fix plan from the rule bundle.
- Retain only identical edits offered by every context contributing to an
  aggregated diagnostic.
- Deduplicate edits and validate file identity, spans, bounds, source content,
  overlaps, and conflicts.
- Prepare all changed contents before replacing any original.
- Use same-directory temporary files and preserve encoding, BOM policy, and
  line endings.
- Regenerate the snapshot through BuildHost after writes.
- Re-run the rules and emit only remaining compact diagnostics.
- Exercise DP1004 replacement and DP1005 argument removal end to end.

Generated documents are already excluded from rule candidates and therefore
cannot enter an edit plan. This slice does not attempt heuristic or LLM-created
fixes.

## Acceptance criteria

- [ ] **s6.1** `drillpress fix` obtains exact edit data only through the
  validated internal bundle protocol.
- [ ] **s6.2** A diagnostic retains a fix only when every contributing context
  offers the identical physical file, span, and replacement.
- [ ] **s6.3** Duplicate identical edits are applied once.
- [ ] **s6.4** Invalid bounds, overlapping edits, conflicting replacements, or
  a source file changed since analysis abort the operation before any original
  is replaced.
- [ ] **s6.5** Failure while preparing any changed file leaves every original
  file untouched.
- [ ] **s6.6** Successful writes use same-directory temporary files and preserve
  each file's encoding, BOM policy, and line endings.
- [ ] **s6.7** DP1004 replaces `string.Empty` with `""` at every common-safe
  location.
- [ ] **s6.8** DP1005 removes `StringComparer.Ordinal` only where the rule's
  speculative-binding check supplied a common-safe edit.
- [ ] **s6.9** After writing, the CLI regenerates the target snapshot and
  re-evaluates all rules.
- [ ] **s6.10** Public stdout contains only findings remaining after the
  recheck; operational progress and failures use stderr.
- [ ] **s6.11** The sample solution builds after all offered fixes are applied.
- [ ] **s6.12** Integration tests cover multi-file success, conflicting edits,
  stale source, partial-preparation failure, multi-target disagreement, and
  recheck behavior.

## Dependencies

Depends on #3 for the structured fix protocol and common-context aggregation,
#4 for snapshot regeneration, and #5 for the complete rules and proposed
fixes.
