# Slice 1: managed end-to-end vertical

This draft PR establishes the smallest complete Drill Press execution path. It
must prove the component boundaries before the authoring model or snapshot
format becomes broad.

## Goal

Run one compiled `string.Empty` rule against the sample project through:

```text
managed CLI -> managed BuildHost -> initial snapshot -> managed rule DLL
```

The result should be a correctly located DP1004 diagnostic produced without
runtime rule parsing, reflection-based discovery, or loading the rule assembly
into the CLI.

## Implementation scope

- Create the production solution, its five production projects, and the sample
  rule project described by the shared implementation plan.
- Add central .NET 10, C# 14, nullable, deterministic-build, package-version,
  and warning settings.
- Define the smallest snapshot DTO needed for the sample project.
- Give BuildHost an initial project-loading/export command.
- Implement the minimum Roslyn-backed member-reference model and strongly typed
  `Members.Are<string>(nameof(string.Empty))` query.
- Define an explicit executable entry point in the sample rule project.
- Implement managed CLI orchestration and the clean/findings/failure exit-code
  contract.
- Add narrow unit and end-to-end tests around this path.

The output and snapshot contracts may initially contain only what this vertical
needs; Slice 3 makes those contracts definitive. Native publication belongs to
Slice 2, and automatic edit application belongs to Slice 6.

## Acceptance criteria

- [ ] **s1.1** `DrillPress.slnx` builds with the pinned .NET 10 SDK and C# 14
  without warnings or errors.
- [ ] **s1.2** Every new production and test project enables nullable reference
  types, deterministic builds, and warnings as errors through shared settings.
- [ ] **s1.3** Project references respect the documented boundaries: the CLI
  references neither Roslyn, MSBuild, nor the sample rule project.
- [ ] **s1.4** BuildHost can load the sample project and write an initial
  compilation snapshot to a caller-supplied path.
- [ ] **s1.5** The public authoring API can express the DP1004 `string.Empty`
  rule with the intended high-level generic syntax.
- [ ] **s1.6** The sample rule DLL uses an explicit entry point and performs no
  reflection-based bundle discovery.
- [ ] **s1.7** `drillpress check` invokes BuildHost and the managed rule DLL
  out of process and removes its temporary snapshot.
- [ ] **s1.8** A violating sample file produces one DP1004 finding at the
  correct physical source location; a compliant file produces none.
- [ ] **s1.9** Exit code `0` means clean, `1` means findings, and `2` means
  invalid input or tool failure.
- [ ] **s1.10** Unit and integration tests cover the positive, negative, and
  orchestration paths.

## Dependencies

None. This is the root implementation slice.
