# Deferred scope and POC differences

The proof of concept deliberately explored a broad architecture. The production
implementation plan narrows that design to the smallest useful technical
preview for an LLM-first lint workflow. This document records why production
may differ from the POC and which capabilities are not being implemented at
this time.

These decisions are not promises that every deferred capability will be added
later. Each should be reconsidered only when usage or measurements demonstrate
its value.

## Runtime and bundle construction

1. **The coordinator CLI is managed.** NativeAOT remains required for rule
   bundles, where the POC measured a material analysis and startup improvement.
   The thin coordinator still launches a managed BuildHost and did not have an
   independently measured NativeAOT benefit.

2. **Rule projects use an explicit entry point.** The proposed Rule SDK source
   generator only removed a small amount of static bootstrap code while adding
   another package, Roslyn generator behavior, diagnostics, and a testing
   surface. A short documented entry point preserves static construction
   without reflection or runtime discovery.

3. **Only supported execution paths are tested.** The implementation does not
   promise every combination of managed and native coordinators and bundles.
   Development uses the managed executable rule DLL; release validation
   compares it with NativeAOT rule bundles on supported platforms.

## Target loading and snapshots

4. **Fast export is the normal path.** Full compiler-diagnostic enumeration is
   opt-in because it accounted for a large share of BuildHost time in the POC.
   Workspace-loading failures are still reported. Callers can request compiler
   validation explicitly when they need it.

5. **There is one target-normalization path.** Rule bundles do not contain a
   second direct loader for files and globs. BuildHost handles every target,
   using MSBuildWorkspace for SDK targets and a lightweight ad hoc mode for
   loose source, then emits the same snapshot shape.

6. **Snapshots are not public input artifacts.** They are ephemeral internal
   messages between BuildHost and a rule bundle. Users are not promised that a
   stored snapshot can be supplied to a later command or tooling version.

7. **Build-only inputs are not copied into the snapshot.** BuildHost may consume
   analyzer configuration and additional files while running a target's source
   generators, but it serializes the resulting sources and effective compiler
   settings rather than copying those inputs when the rule model cannot query
   them.

8. **The snapshot has no migration or backward-compatibility system.** It keeps
   only a magic identifier and exact integer format marker so mismatched
   processes fail safely. A mismatch is resolved by using matching tools and
   regenerating the ephemeral snapshot.

9. **Snapshot caching and a persistent BuildHost are deferred.** Correctly
   invalidating every MSBuild, SDK, generator, reference, and source input is a
   substantial correctness problem. The uncached implementation will be
   measured before either optimization is designed.

## Rule and model scope

10. **The optional `DateTime.Now` and `Thread.Sleep` rules are omitted.** The
    required rules already exercise syntax, semantic identity, cross-project
    analysis, exceptions, and automatic fixes. More convention rules can be
    added after the engine API stabilizes.

11. **The first type-identity surface is focused.** It supports the strongly
    typed BCL and constructed-generic forms needed by real rules, named metadata
    identities for target types, and optional assembly qualification.
    Exhaustive CLR type shapes and direct references to external target
    contracts are deferred until concrete rules require them.

12. **Generated documents are not rule candidates.** They remain in
    compilations so semantic binding is correct, but reporting uneditable
    generated-code violations would consume LLM context without identifying
    the generator input that must change.

## Output and benchmarking

13. **The CLI exposes one public diagnostic format.** The compact grouped format
    is the LLM-facing contract. Exact spans, context identities, and edits
    travel through a structured internal bundle protocol only, avoiding a
    public matrix of minimal, detailed, and context-enhanced renderers.

14. **Pinned repository measurements are evidence, not absolute release
    gates.** Exact finding counts can change with legitimate rule corrections,
    and wall time varies by hardware, SDK patch, filesystem state, and system
    load. Benchmarks retain signatures and raw measurements for same-machine
    comparisons without hard-coded timing or count thresholds.

## Platforms and release scope

15. **Windows and Linux are supported; macOS is not in the current scope.** Both
    supported platforms run integration tests and publish NativeAOT rule
    bundles.

16. **The POC is a historical reference rather than a permanent CI target.** It
    remains in source control with its documentation and measurements, but the
    production implementation does not take on an indefinite obligation to
    update and build it with future dependencies and SDKs.

17. **The first release gate covers the focused technical preview.** Package
    distribution, a rule-project template, release automation, release notes,
    configurable process timeouts, and an exhaustive crash and cancellation
    matrix are deferred. They do not block feedback on the complete core
    workflow: author rules, compile a bundle, check real targets, emit compact
    findings, apply common-safe fixes, and recheck.
