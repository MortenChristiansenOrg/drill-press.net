# Drill Press implementation plan

## Outcome

Build the production version of Drill Press as a .NET 10 and C# 14 rule engine
whose primary consumer is an LLM. A rule author writes composable, strongly
typed C# rules; compilation produces an executable rule DLL with no runtime
rule parsing or reflection-based discovery. A small NativeAOT CLI evaluates a
target through a managed MSBuild front end, runs the compiled rule bundle out
of process, and emits the minimum text needed to locate and correct violations.

The completed `AOT POC` is an executable specification and benchmark baseline.
Production code will be created in new root `src`, `tests`, and `benchmarks`
folders. POC code should not be moved or incrementally renamed into production
projects: concepts may be ported deliberately, with tests, while the POC remains
available for comparison.

## Fixed architectural decisions

- Rule definitions are C# 14. There is no English parser, standalone DSL
  parser, expression-tree compiler, runtime source compilation, or language
  server in the initial implementation.
- The rule DLL is an executable process contract. The CLI never loads arbitrary
  rule assemblies into its own process.
- `DrillPress.RuleSdk` supplies an incremental source generator. Exactly one
  parameterless static factory marked `[RuleBundle]` returns the bundle's
  `RuleSet`; the generator emits the process entry point as a direct method
  call. A project property can disable generation for an explicitly authored
  entry point, but reflection discovery is never the fallback.
- The CLI and rule bundle support NativeAOT. MSBuild and `MSBuildWorkspace` stay
  in a separate managed BuildHost process.
- Compiler-faithful solution and project analysis uses an immutable, versioned
  compilation snapshot. Files and globs may use the deliberately limited direct
  loader.
- One Roslyn-backed model supports syntax, semantic, project, and cross-project
  rules. Rule authors do not inherit from node- or operation-specific base
  classes.
- Every project/target context is analyzed independently. Equivalent physical
  findings are aggregated only after evaluation. An automatic edit is exposed
  only when the same edit is safe in every contributing context.
- Default output is grouped for LLM consumption. JSONL, exact spans, edits,
  compilation contexts, and profiling are opt-in.
- Roslyn analyzer packaging is out of scope. The product is a CLI plus compiled
  rule bundles.

## Proposed repository layout

```text
DrillPress.slnx
src/
  DrillPress.RuleAuthoring/   Public rule DSL, descriptors, CodeType
  DrillPress.Engine/          Roslyn model, query planner, evaluation, fixes
  DrillPress.Manifest/        Versioned snapshot DTOs and generated JSON
  DrillPress.BuildHost/       Managed MSBuildWorkspace front end
  DrillPress.Cli/             Small NativeAOT coordinator
  DrillPress.RuleSdk/         Build-time bundle bootstrap/catalog generation
samples/
  DrillPress.SampleRules/     Representative rules and executable rule DLL
tests/
  DrillPress.UnitTests/
  DrillPress.ConformanceTests/
  DrillPress.IntegrationTests/
  Fixtures/
benchmarks/
  DrillPress.Benchmarks/
  run-xunit.sh
AOT POC/                     Frozen executable specification
Sample Solution/             Small end-to-end target
```

`DrillPress.RuleAuthoring` should keep its public surface small. Roslyn types
may appear on advanced model objects when they materially improve rule
expressiveness, but authoring helpers should prefer stable Drill Press types.
`DrillPress.Manifest` must not depend on MSBuild. `DrillPress.Cli` must not
reference Roslyn, MSBuild, or a rule assembly.

## Delivery sequence

### Phase 1: contracts and vertical skeleton

Create the production solution, central build settings, process contracts, and
one end-to-end rule. Make the managed BuildHost, NativeAOT CLI, and NativeAOT
rule bundle work together before expanding the model. Lock the LLM and JSONL
golden formats immediately.

### Phase 2: authoring model and required rules

Port the composable query/condition/location/fix model, generic `CodeType`
identity, exceptions, and query-planning constraints. Implement the seven POC
rules using only the intended public API. Add focused model tests as each
primitive is introduced.

### Phase 3: compiler-faithful snapshots

Implement BuildHost capture and AOT reconstruction, including source generators,
linked files, analyzer configuration, project references, uncommon metadata
reference properties, evaluated test classification, and compiler validation.
Version the format from its first production revision and add migration/error
tests.

### Phase 4: aggregation and safe correction

Implement context-aware diagnostic aggregation, minimal default rendering,
detailed JSONL, edit intersection, conflict detection, atomic writes, snapshot
regeneration, and recheck. Exercise linked and multi-target source explicitly.

### Phase 5: performance and incremental operation

Port query planning and implementation indexes, then profile rather than guess.
Add content-addressed snapshot caching and/or a persistent BuildHost only after
the uncached path is correct. Compare every optimization against diagnostic and
fix signatures, not only counts.

### Phase 6: hardening and release

Add cancellation, timeouts, cross-platform coverage, deterministic packaging,
templates, documentation, compatibility policy, and release automation. Publish
preview packages only after NativeAOT and real-repository gates pass.

## Acceptance criteria

- **A. Foundation and build**

  - **a.1** A root `DrillPress.slnx` builds on the pinned .NET 10 SDK with no
    warnings or errors.
  - **a.2** Every production and test project enables nullable reference types,
    C# 14, deterministic builds, and warnings as errors through shared build
    properties.
  - **a.3** The production projects follow the dependency boundaries in the
    proposed layout; in particular, the CLI has no Roslyn, MSBuild, or rule
    project reference.
  - **a.4** `AOT POC` remains buildable and is not referenced by production
    projects.
  - **a.5** CI performs Release build, test, formatting, NativeAOT publish, and
    repository hygiene checks on every supported change.

- **B. Rule authoring API**

  - **b.1** A rules project can define reusable queries with
    `Code.Methods.Where(...)`, `Code.Interfaces`, `Code.Types`, and
    `Code.MemberReferences` without inheriting from an engine base class.
  - **b.2** Conditions compose with `And`, `Or`, `Not`, and `ExceptWhen`, and
    composition preserves both semantics and safe query-planning constraints.
  - **b.3** A reusable xUnit-test query can be declared once and used by
    multiple rules.
  - **b.4** Each rule declares a stable ID, remediation message, condition,
    optional location selector, and optional fix in one readable definition.
  - **b.5** Duplicate or empty rule IDs and empty remediation messages fail at
    bundle construction with actionable errors.
  - **b.6** Public API documentation includes short examples suitable for an
    LLM to imitate without relying on internal engine types.

- **C. Strong type identity and external references**

  - **c.1** Helpers accept strongly typed forms such as `Members.Are<string>()`,
    `Methods.Return<List<string>>()`, and `Types.Implement<IContract>()`.
  - **c.2** Closed generic arguments, nested types, arrays, nullable value
    types, and assembly identity are represented deterministically and matched
    against Roslyn symbols.
  - **c.3** Named type identities support target-project types when a direct
    project reference would be undesirable or cyclic.
  - **c.4** A rules project may reference a stable external contracts project
    and use those types in generic rule helpers without runtime discovery.
  - **c.5** Type identity creation performs no assembly scanning and requires no
    reflection-based rule discovery; any use of `typeof(T)` is limited to
    compile-time-selected type metadata and is NativeAOT-safe.

- **D. Rule bundle compilation and process contract**

  - **d.1** Building a rules project produces an executable `.dll` that accepts
    `check` and `fix` and can also be published as a native executable.
  - **d.2** The Rule SDK generates an entry point that directly calls the single
    parameterless static `[RuleBundle]` factory returning `RuleSet`; an explicit
    entry point is supported only when generation is disabled deliberately.
  - **d.3** Missing, duplicate, non-static, parameterized, or incorrectly typed
    bundle factories produce precise compile-time diagnostics.
  - **d.4** The managed DLL and NativeAOT executable produce byte-identical
    diagnostic output for the same snapshot and options.
  - **d.5** The process contract reserves exit code `0` for clean, `1` for
    findings, and `2` for invalid input or analysis failure.

- **E. Compiler-faithful BuildHost and manifest**

  - **e.1** BuildHost opens `.sln`, `.slnx`, and `.csproj` targets through the
    registered .NET SDK and `MSBuildWorkspace`.
  - **e.2** The snapshot captures C# parse and compilation options,
    preprocessor symbols, ordinary and generated source, linked source,
    additional files, analyzer configuration, metadata references, and project
    references.
  - **e.3** Metadata references preserve aliases, embed-interop settings, and
    other properties required to reconstruct binding equivalently.
  - **e.4** Source generators complete before source capture, and generated
    documents remain identifiable after AOT reconstruction.
  - **e.5** `IsTestProject` is evaluated per project/target context. Explicit
    evaluated `true` and `false` override naming and path heuristics; fallback
    inference is used only when MSBuild leaves the property unset.
  - **e.6** The default validated export records compiler error counts and
    rejects compiler-invalid or workspace-invalid targets unless the caller
    explicitly selects the documented relaxation.
  - **e.7** Fast export marks compiler diagnostics as unevaluated rather than
    representing them as zero.
  - **e.8** The manifest is versioned from revision 1, uses source-generated
    JSON serialization, rejects unsupported versions clearly, and is written
    atomically.
  - **e.9** Reconstructed projects match BuildHost compiler-error counts and
    rule signatures across the realistic fixture and pinned xUnit revision.
  - **e.10** Documentation states that snapshots contain source and
    machine-local paths and must be treated as sensitive, non-portable build
    artifacts.

- **F. Code model and query execution**

  - **f.1** One `AnalysisSolution` model exposes methods, assertions,
    interfaces, named types, member references, documents, projects, and
    source locations required by all sample rules.
  - **f.2** Semantic models and discovered entity collections are lazy and
    cached within one analysis without changing observable results.
  - **f.3** Member-name conditions contribute internal candidate constraints so
    irrelevant identifiers are not semantically bound; unfiltered queries
    retain complete behavior.
  - **f.4** Cross-project implementation lookup uses a shared index rather than
    rescanning every type for every interface.
  - **f.5** Query optimizations are correct for aliases, qualified names,
    generic member access, conditional source, linked source, and multiple
    target frameworks.
  - **f.6** Profiling can report load, preparation, per-rule, fix, output, and
    total timings on stderr without changing stdout.

- **G. Required behavioral rules**

  - **g.1** An xUnit test with more than two empty lines produces DP1001 at the
    first disallowed empty line.
  - **g.2** An xUnit assertion before the final empty line produces DP1002 at
    the first misplaced assertion.
  - **g.3** A sole `Assert.Throws` is exempt from DP1002 even when it occurs
    before the second empty line; other assertions and multiple assertions are
    not exempt.
  - **g.4** An interface with exactly one concrete non-test implementation
    produces DP1003, using evaluated test-project classification.
  - **g.5** `string.Empty`, `StringComparer.Ordinal`, `DateTime.Now`, and
    `Thread.Sleep` produce DP1004 through DP1007 using semantic identity rather
    than source spelling.
  - **g.6** DP1004 offers replacement with `""`; DP1005 offers argument removal
    only when speculative binding proves the rewritten invocation valid.
  - **g.7** Each required rule has positive, negative, alias, generated-source,
    and relevant multi-project or multi-target tests.

- **H. LLM-first diagnostic output**

  - **h.1** Default output groups by rule and file, prints each remediation
    message once, and emits only line plus non-default column for each physical
    location.
  - **h.2** A leading `+` marks a location with an automatic edit that is safe
    in every contributing compilation; no verbose legend or summary is emitted
    per run.
  - **h.3** A clean run writes no diagnostic text to stdout.
  - **h.4** Equivalent rule/file/span/message findings from linked or
    multi-target source collapse to one default location after all contexts
    have been evaluated.
  - **h.5** `--format jsonl` emits only rule, file, line, column, and message by
    default; constant schema version and severity fields are absent.
  - **h.6** `--details` opts JSONL into exact spans and common-safe edits. A
    same-file edit does not repeat the diagnostic file path.
  - **h.7** `--include-contexts` opts into deterministic project/target
    identities for both LLM and JSONL output.
  - **h.8** Output ordering is deterministic by rule, file, and span on every
    supported operating system.
  - **h.9** On the pinned xUnit snapshot, default output represents all 1,547
    currently known actionable locations in at most 60 KB and remains at least
    90% smaller than detailed unaggregated JSONL.

- **I. Fix safety and rechecking**

  - **i.1** Context aggregation retains an edit only if an identical file,
    span, and replacement is offered by every contributing violation.
  - **i.2** Generated-source diagnostics never offer edits.
  - **i.3** Before writing, the engine deduplicates edits, validates bounds,
    rejects overlaps or conflicting replacements, and verifies that source has
    not changed since analysis.
  - **i.4** Multi-file fix preparation completes successfully before any
    original file is replaced; failed preparation leaves all originals intact.
  - **i.5** Files are written through same-directory temporary files while
    preserving their original encoding, BOM policy, and line endings.
  - **i.6** Fixing a solution/project regenerates its snapshot and rechecks the
    changed target; stdout contains only remaining findings.
  - **i.7** A directly supplied immutable snapshot cannot be fixed and produces
    an actionable error directing the caller to the original target.
  - **i.8** The sample solution builds after all offered fixes are applied.

- **J. CLI orchestration**

  - **j.1** The CLI accepts a solution, project, directory, C# file, glob, or
    snapshot plus a compiled rule DLL/native bundle.
  - **j.2** Solutions, projects, and directories automatically flow through
    BuildHost; files, globs, and snapshots avoid MSBuild when possible.
  - **j.3** BuildHost resolution supports an explicit option, an environment
    variable, and a documented adjacent deployment layout.
  - **j.4** `--property Name=Value`, validated/fast export, compiler-error
    relaxation, profiling, format, detail, and context options are forwarded
    without leaking coordinator-only arguments to the wrong process.
  - **j.5** Temporary snapshots use unique restricted locations and are removed
    after success, findings, cancellation, or failure.
  - **j.6** Rule stdout passes through unchanged for `check`; operational
    messages and profiling use stderr.
  - **j.7** `fix` obtains detailed JSONL internally, applies only common-safe
    edits, regenerates the snapshot, and renders the caller's requested final
    format.
  - **j.8** Cancellation and configurable child-process timeouts terminate the
    process tree and return a tool-failure exit without leaving temporary
    artifacts.

- **K. Performance and caching**

  - **k.1** Benchmarks record wall time, user/system CPU, peak RSS, manifest
    size, context findings, actionable locations, and safe fixes.
  - **k.2** The pinned xUnit benchmark is reproducible from a full clone with
    initialized submodules and records upstream restore/workspace failures
    rather than hiding them.
  - **k.3** An uncached NativeAOT xUnit analysis does not regress by more than
    10% from the POC's 25.14-second rule-evaluation baseline on comparable
    hardware and warm-cache conditions.
  - **k.4** Query-planning changes must produce identical aggregated diagnostic
    and common-fix signatures before and after optimization.
  - **k.5** Snapshot caching keys include project/import inputs, relevant MSBuild
    properties, SDK identity, references, analyzer configs, additional files,
    generators, and rule-engine schema version.
  - **k.6** A cache hit never reuses source or semantic results after a relevant
    input changes; invalidation has targeted integration tests.
  - **k.7** Performance gates report regressions but retain raw artifacts and
    phase timing so a slow phase can be identified.

- **L. Testing and compatibility**

  - **l.1** Unit tests cover type identity, condition algebra, query constraints,
    aggregation, fix intersection, path handling, argument parsing, and output
    rendering.
  - **l.2** Conformance tests cover direct loading, faithful snapshots, source
    generators, linked files, additional files, analyzer config, per-tree
    compiler severity, test classification, and generated-source safety.
  - **l.3** Golden tests lock the default LLM output and minimal/detailed/context
    JSONL contracts, including exact byte output and deterministic ordering.
  - **l.4** Integration tests run managed and NativeAOT coordinators and bundles
    in every supported combination and compare outputs byte for byte.
  - **l.5** Tests run on Linux, Windows, and macOS for path casing, separators,
    executable launching, temporary files, and atomic replacement behavior.
  - **l.6** Manifest compatibility policy states which versions can be read,
    how unsupported versions fail, and when schema changes require regeneration.
  - **l.7** Failure-path tests cover invalid rules, invalid targets, compiler
    errors, workspace failures, malformed bundle output, child crashes,
    cancellation, timeouts, conflicting edits, and stale source.

- **M. Packaging, documentation, and release**

  - **m.1** Preview artifacts include the NativeAOT CLI, managed BuildHost and
    dependencies, Rule Authoring package, Rule SDK/build package, and a sample
    rule template.
  - **m.2** A fresh repository can create, compile, publish, and run a rule
    bundle by following one short documented path without copying internal POC
    code.
  - **m.3** CLI help is concise and shows the LLM-first default before secondary
    JSONL, context, profiling, and compiler-relaxation options.
  - **m.4** Documentation explains trust boundaries: a rule bundle is executable
    code, snapshots contain source, and fixes modify original files.
  - **m.5** Package and executable versions are deterministic and reported on
    stderr only when explicitly requested.
  - **m.6** Release notes include compatibility changes, benchmark deltas, known
    limitations, and the exact pinned acceptance repositories.
  - **m.7** The first preview is released only when every criterion in groups
    A–L and criteria m.1–m.6 pass; any approved exception is documented with an
    owner and follow-up milestone.

## Initial implementation slices

The first pull requests should stay vertically testable:

1. Create the root solution and projects; implement one `string.Empty` rule from
   authoring API through NativeAOT CLI output (`a`, `b`, `d`, and `j` basics).
2. Establish the LLM/JSONL golden contract and context aggregation before more
   rules depend on it (`h.1`–`h.8`).
3. Implement BuildHost snapshot v1 and reconstruction with the realistic and
   classification fixtures (`e.1`–`e.10`).
4. Port the full model, query planner, generics, exceptions, and seven sample
   rules (`b`, `c`, `f`, and `g`).
5. Implement context-safe fixes and faithful recheck (`i` and `j.7`).
6. Run the xUnit acceptance benchmark, optimize only measured hotspots, then
   add caching (`k`).
7. Complete failure paths, cross-platform jobs, packaging, templates, and
   preview documentation (`l` and `m`).

## Definition of implementation-ready

This plan is implementation-ready; work begins with slice 1. Changes to an
acceptance criterion should preserve its ID so discussions and pull requests
can refer to it unambiguously. Retired criteria should be marked retired rather
than renumbering later criteria.
