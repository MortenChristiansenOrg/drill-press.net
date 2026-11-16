# C# 15 unions in Drill Press

This note records which uses of C# 15 unions and closed hierarchies Drill Press
adopts, and why. Rule authors find the how-to in the manual's
[union section](../user-docs/selections.html#union-candidates).

## Recommendation

| Candidate | Decision | Reason |
| --- | --- | --- |
| Mixed candidates in consumer rules | Adopt as client guidance | One clause can group and fix across unrelated library types, with compiler-checked case handling and no engine change. |
| Analyzing C# 15 target source | Adopt | Projects, snapshots, fixes and test workspaces already carry C# 15; loose files now parse as C# 15. |
| Typed reporting targets in the public API | Reject | `ReportAt` overloads already check targets at compile time. |
| `FixAttempt` union for withheld fixes | Reject | `FixProposal.Withheld(reason)` already models refusal, and `--explain-fixes` shows it. |
| Unions or closed hierarchies for `ProofResult` | Reject | Its cases carry no data; `CheckOutcome` pairs it with the reason. |
| Building Drill Press itself with C# 15 | Defer to a .NET 11 move | Nothing above needs it, and C# 15 needs the .NET 11 SDK on every contributor and CI runner. |

### Mixed candidates

A policy such as "read time through the injected `TimeProvider`" covers
`MemberReference` reads and `CodeInvocation` waits. Drill Press owns both types and
seals them, so a consumer cannot give them a common base. Before unions, a rule
had three options:

- **Typed clauses under one identity.** Each clause keeps its own conditions and
  evidence, but `ReportOncePer` cannot group findings across clauses.
- **An upcast to `ICodeElement`.** One clause, but the fix factory needs a discard
  arm, so a new form compiles without a fix.
- **A wrapper record hierarchy.** One clause and named cases, but one allocation per
  candidate, and C# 14 still needs a discard arm.

`union ClockUse(MemberReference, CodeInvocation)` keeps one clause, groups across
forms and reports CS8509, naming the missing case, when a switch does not handle
one. It needs no engine support. The union struct does not implement
`ICodeElement`, so the clause reports with
`ReportAt(use => use.Value as ICodeElement)`, the existing rule for computed
candidates. A union can implement `ICodeElement` itself by forwarding `Location`
and `Source`, but that is more code than the `ReportAt` line.

The manual example is compiled and run by
`UserManualTests.Union_example_reports_once_per_method_and_fixes_both_source_forms`:
a method that waits and reads the clock gets one finding, and the fixes for both
uses are validated and applied together.

Closed hierarchies (`closed class`) fit cases the author owns that share data or
behavior. They cannot close over Drill Press's sealed candidate types, so they do
not replace unions for mixed candidates.

### Typed reporting targets

The issue proposed `union ReportingTarget(ICodeElement, SourceLocation, SyntaxNode,
SyntaxToken)` in place of `Func<T, object>` selectors. Reporting no longer has
untyped selectors: `ReportAt` has one overload per target kind, so an unsupported
target is already a compile error. A union parameter would also need C# 15 at every
call site to get the implicit conversions, would box `SyntaxToken`, and would add the
default value (`Value` is null) as a new runtime failure. `ReportOncePer` keys stay
generic; any equatable key is valid.

### Withheld fixes

`FixProposal` is already a closed two-state value: a proposal with edits, or a
withheld one with a reason. Creation is separate from approval: `FixProposal`
values pass through conflict checks, combined-batch proofs and compilation
validation in every affected context before they are applied, and those later
refusals carry their own reasons. A `FixAttempt` union would change every factory's
return type, a breaking change for all bundles, while adding nothing the validator
needs. Typed refusal reasons were not adopted either: reasons are single-line text
for the reader of `--explain-fixes`, they name the failed gate and source position,
and no consumer branches on them. `RuleTestResult.ExplainedOutput` lets tests assert
them.

`ProofResult` stays an enum (`Proven`, `Unknown`, `Disproven`). The internal
`CheckOutcome` already pairs it with an optional reason.

## Measurements

A scratch program built with the .NET 11 SDK (the repository builds with C# 14)
evaluates the clock policy, using the manual example's queries and fix factories,
over one generated file with a wait and a
read in each method, reusing one analysis. Each variant ran 20 measured rounds after
3 warm-up rounds, interleaved, through `RuleCatalog.Evaluate` on .NET 10.0.15 (x64,
Release, workstation GC). Times are medians with the 25th to 75th percentiles in
parentheses; allocations are medians per evaluation.

| Variant | Without fixes | Allocated | With `UseTimeProvider` fixes | Allocated |
| --- | --- | --- | --- | --- |
| Typed clauses | 6.2 ms (5.1–8.3) | 457 KiB | 4.49 s (4.08–5.11) | 804.1 MiB |
| Union | 5.9 ms (5.1–8.1) | 524 KiB | 4.37 s (4.14–4.87) | 804.1 MiB |
| Record hierarchy | 5.8 ms (5.3–8.2) | 549 KiB | 4.33 s (3.85–5.54) | 804.1 MiB |
| `ICodeElement` upcast | 5.7 ms (5.1–7.4) | 524 KiB | 4.30 s (3.89–5.54) | 804.1 MiB |

With 800 candidates (400 methods), the variants take the same time within noise:
fix proofs dominate, and without fixes discovery and binding do. The union allocates
exactly as much as the `ICodeElement` upcast, about 85 bytes per candidate more than
typed clauses. That cost comes from the extra `Select` and `Concat` stages, not from
the union: its reference-type cases are stored without boxing, and the engine's type
tests on the struct allocate nothing. The record wrapper adds one object per
candidate on top. The machine was shared with other work, so the times only show
that nothing regressed; they are not a benchmark.

## Target-source support

- **Projects.** MSBuild loading records each project's language version in the
  snapshot, and reconstruction parses with it. Roslyn 5.9 supports C# 15, so a
  `net11.0` project or `<LangVersion>15.0</LangVersion>` loads unions and closed
  types without changes. The CLI needs the .NET 11 SDK to load a `net11.0` project.
- **Loose files and globs** now parse as C# 15 instead of C# 14. They compile against
  the BuildHost runtime: on .NET 10 the sources must declare the union and closed-type
  attributes themselves, or the compilation reports missing members.
- **Test workspaces.** `RuleTestWorkspace.AddProject` defaults to C# 14 and accepts
  `languageVersion: LanguageVersion.CSharp15`. Its references come from the test
  host, so a .NET 10 host needs the same declarations in the test sources.

Tests cover a union and a closed class in loose source, and a validated fix inside
a union member through `RuleTestWorkspace`.

## Requirements for client bundles

- Unions and closed classes need C# 15, which ships with the .NET 11 SDK. Drill
  Press packages target `net10.0` and work in either kind of bundle.
- A bundle that targets `net11.0` gets `UnionAttribute`, `IUnion` and
  `IsClosedTypeAttribute` from the runtime. A `net10.0` bundle sets
  `<LangVersion>15.0</LangVersion>` and declares the types it uses in
  `System.Runtime.CompilerServices`. The manual shows the union declarations.
- Union values are structs; their default value holds null. Drill Press queries
  never produce default values, so the example omits a null arm.
- Native bundles need nothing extra: the union is ordinary code in the bundle, and
  the engine reads no reflection metadata from it.
- Exhaustive handling shows that every form gets a factory. It does not show that
  a fix preserves behavior, and it does not replace the per-context checks.
  Every proposal still passes its proof and combined validation.
