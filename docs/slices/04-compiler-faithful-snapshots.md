# Slice 4: compiler-faithful snapshots

This draft PR replaces the minimal Slice 1 loader with the production BuildHost
pipeline. Every supported target shape must be normalized into the contract
locked by Slice 3 before the rule bundle sees it.

## Goal

Reconstruct Roslyn compilations in the rule process with the source, options,
references, project graph, generated documents, and evaluated project metadata
needed for the same rule decisions as the BuildHost compilation.

## Implementation scope

- Register and use the installed .NET SDK with `MSBuildWorkspace` for solution
  and project targets.
- Resolve directory targets and preserve every project/target-framework
  context.
- Build documented ad hoc compilations for C# files and globs.
- Capture parse and compilation options, preprocessor symbols, ordinary and
  linked source, generated source, metadata references, and project references.
- Preserve metadata-reference aliases, embed-interop settings, and other
  binding-relevant properties.
- Complete target source generators before capture without copying
  build-only inputs into the snapshot.
- Evaluate `IsTestProject` per project context, using inference only when the
  evaluated property is absent.
- Make fast export the default and compiler-diagnostic enumeration opt-in.
- Write snapshots atomically to restricted temporary locations.
- Add realistic fixtures and compare reconstructed semantic rule signatures.

Generated documents remain part of compilation and binding but must not become
rule candidates. Snapshots remain ephemeral internal artifacts rather than
user-supplied CLI inputs.

## Acceptance criteria

- [ ] **s4.1** BuildHost accepts `.sln`, `.slnx`, `.csproj`, directory, C#
  file, and glob targets and emits the same snapshot envelope for each.
- [ ] **s4.2** SDK targets are opened through the registered .NET SDK and
  `MSBuildWorkspace`; loose-source defaults are explicit and documented.
- [ ] **s4.3** Every evaluated project and target-framework context is retained
  with its effective parse options, compilation options, and symbols.
- [ ] **s4.4** Ordinary, linked, and generated source documents reconstruct
  with stable physical paths and generated-source identity.
- [ ] **s4.5** Metadata and project references reconstruct equivalent symbol
  binding, including aliases and embed-interop properties.
- [ ] **s4.6** Target source generators complete before capture and their output
  participates in semantic binding after reconstruction.
- [ ] **s4.7** Generated documents are excluded from all rule-candidate
  collections.
- [ ] **s4.8** Explicit evaluated `IsTestProject=true` and `false` override
  naming heuristics; inference runs only when the property is absent.
- [ ] **s4.9** Default export reports workspace-loading failures without
  enumerating every compiler diagnostic.
- [ ] **s4.10** `--validate-compilation` calculates compiler errors and rejects
  an invalid target before rule evaluation.
- [ ] **s4.11** Snapshot files are written atomically, treated as sensitive, and
  removed by the CLI after success, findings, or failure.
- [ ] **s4.12** Reconstructed projects preserve the BuildHost's semantic rule
  signatures across the realistic fixture and pinned xUnit revision.
- [ ] **s4.13** Conformance tests cover conditional source, linked files,
  generated documents, project references, metadata-reference properties,
  per-tree compiler severity, and test-project classification.

## Dependencies

Depends on #1 for orchestration and #3 for the snapshot and result contracts
that this loader must implement.
