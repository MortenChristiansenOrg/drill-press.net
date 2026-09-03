# Drill Press implementation plan

## Outcome

Build Drill Press as a .NET 10 and C# 14 rule engine whose primary consumer is
an LLM. Rule authors write composable, strongly typed C# rules that compile
into an executable rule DLL. A managed CLI checks a target with that bundle and
emits the minimum deterministic text needed to locate and correct violations.
Rule bundles can also be published with NativeAOT.

The production design deliberately differs from the proof of concept in
several places. [POC_DIFFERENCES.md](POC_DIFFERENCES.md) records those scope
decisions and their rationale.

## Architecture

```text
target
  -> managed CLI
  -> managed BuildHost
  -> ephemeral compilation snapshot
  -> compiled rule bundle
  -> structured internal result
  -> compact diagnostics or safely applied fixes
```

The BuildHost exits before rule evaluation, releasing the memory used by
MSBuild and `MSBuildWorkspace`. The CLI never loads rule assemblies, Roslyn,
or MSBuild into its own process. The rule bundle never discovers or evaluates
raw project inputs.

## Rule authoring

- Rules are ordinary C# 14 code. There is no runtime rule parser, runtime source
  compilation, expression-tree compiler, or reflection-based rule discovery.
- Each rule project has an explicit entry point that constructs its `RuleSet`
  and calls the engine.
- The public API uses reusable queries, composable conditions, location
  selectors, exceptions, and optional fixes. Rule authors do not inherit from
  syntax- or operation-specific engine base classes.
- One Roslyn-backed `AnalysisSolution` model supports syntax, semantic,
  project, and cross-project rules.
- Generic helpers provide strongly typed identities for common BCL and
  constructed-generic types. Named metadata identities cover target-project
  types without introducing project-reference cycles.
- Every rule has a stable ID and a concise remediation message that tells an
  LLM what the code should do instead.

## Process and snapshot contracts

- The CLI and BuildHost are managed .NET applications. Executable rule DLLs
  support managed execution and NativeAOT publication on Windows and Linux.
- Solutions, projects, directories, files, and globs all flow through
  BuildHost and produce the same snapshot shape. SDK targets use
  `MSBuildWorkspace`; loose source uses a documented ad hoc compilation.
- Snapshots are ephemeral internal artifacts. They carry a magic identifier and
  one exact integer format marker so mismatched components fail immediately.
- BuildHost captures the effective compiler inputs required to reconstruct
  Roslyn compilations. Target source generators run before capture; their
  generated documents participate in semantic binding but are not rule
  candidates.
- Fast snapshot export is the normal path. Full compiler-diagnostic validation
  is explicitly requested when needed.
- Rule bundles run out of process and consume only compatible snapshots. Their
  structured response contains the exact locations and common-safe edits needed
  by the CLI.

## Evaluation, output, and fixes

- Every project and target-framework context is evaluated independently because
  symbols, references, compiler options, and conditional source may differ.
- Equivalent findings are aggregated by physical source location only after all
  contributing contexts have been evaluated.
- An automatic edit is exposed only when every contributing context proposes
  the same file, span, and replacement.
- The public CLI has one compact, deterministic output format grouped by rule
  and file. Messages are printed once, a clean run emits no diagnostic text,
  and verbose context or protocol data never consumes the LLM's output budget.
- Public diagnostics use stdout. Operational errors and opt-in profiling use
  stderr. Exit codes distinguish clean results, findings, and tool failures.
- `fix` applies every common-safe, non-conflicting edit, preserves source
  encoding and line endings, regenerates the snapshot, and reports only
  remaining findings.

## Performance and correctness direction

- Correct semantic results and safe edits take precedence over speed.
- Candidate selection and query optimization may avoid unnecessary Roslyn
  binding, but optimized and unoptimized executions must have identical
  diagnostic and fix signatures.
- The pinned xUnit repository is the representative scale workload. Benchmark
  reports retain raw phase timings, CPU, peak memory, snapshot size, finding
  counts, safe-fix counts, and output size for same-machine comparisons.
- Benchmark measurements guide optimization; hardware-dependent timings and
  exact finding counts are not release contracts.
- Windows and Linux are supported and run the managed and NativeAOT integration
  paths.

## Repository layout

```text
DrillPress.slnx
src/
  DrillPress.RuleAuthoring/   Public rule API, descriptors, CodeType
  DrillPress.Engine/          Roslyn model, candidate selection and query optimization, evaluation, fixes
  DrillPress.Manifest/        Snapshot DTOs, format marker, generated JSON
  DrillPress.BuildHost/       Managed target loader and MSBuildWorkspace front end
  DrillPress.Cli/             Managed coordinator
samples/
  DrillPress.SampleRules/     Representative rules and executable rule DLL
tests/
  DrillPress.UnitTests/       xUnit 3 unit tests
  DrillPress.ConformanceTests/
  DrillPress.IntegrationTests/
  Fixtures/
benchmarks/
  DrillPress.Benchmarks/
  run-xunit.sh
Sample Solution/             Small end-to-end target
```

`DrillPress.RuleAuthoring` keeps its public surface small.
`DrillPress.Manifest` has no MSBuild dependency. `DrillPress.Cli` has no
Roslyn, MSBuild, or rule-project dependency. `DrillPress.BuildHost` is the
only target loader, and `DrillPress.Engine` is the only snapshot consumer.

## Delivery direction

Implementation proceeds through dependent, vertically testable pull requests.
Each PR owns its detailed scope, dependencies, and acceptance criteria. Across
all PRs:

- Production and test projects use nullable reference types, C# 14,
  deterministic builds, and warnings as errors.
- Changes include tests at the narrowest useful level and preserve deterministic
  public output.
- NativeAOT-sensitive changes are validated in both managed and native rule
  execution.
- Performance changes prove diagnostic and fix equivalence before their timing
  result is considered.
- User-facing behavior is documented in the same PR that introduces it.
