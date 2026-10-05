# Test-backed coverage requirements

Use the ordinary rule API to require tests to exercise critical source occurrences:

```csharp
rules
    .For(Code.Calls.Where(call => call.Target.Name == "Commit"))
    .Require(Coverage.Executed, "COV001", "Exercise each commit in tests.");

rules
    .For(Code.Files.Where(file => file.Folder.EndsWith("/Domain")))
    .Require(Coverage.Line.AtLeast(90), "COV002", "Exercise domain code.");

rules
    .For(Code.Projects.Where(project => !project.IsTestProject))
    .Require(Coverage.Line.AtLeast(80), "COV003", "Exercise production code.");
```

Calls, member references, and other `ICodeElement` occurrences use the same
execution requirement. Line thresholds also work on source methods, types and
syntax nodes. Coverage requirements convert to `RuleCondition<T>` and retain
collection metadata through Boolean conditions and built-in query composition.
Custom selectors and predicates must remain pure; do not run tests inside them.

`AnalysisEngine` and executable rule bundles arrange collection before evaluating
coverage conditions. No work is collected for rule sets without coverage
requirements or with zero source-selected coverage candidates. Collection is limited
to the evaluated contexts containing those candidates. The first relevant coverage check automatically installs the pinned Microsoft
coverage tool into Drill Press's user-local cache. It requires an SDK, NuGet
access on first use, and working test projects; rule authors do not add collector
packages or configure report formats. SDK source snapshots must already be
restored as usual. Test runs build their projects and dependencies. A failed
installation, evaluation, build, test run or collection fails the check explicitly.
Cancellation stops external processes and removes temporary reports.

Planning uses a separate query/cache lifetime. Source predicates, projections,
unions, and anchored custom values retain their context; built-in coverage
conditions remain unresolved possibilities until evidence is ready, including
negation and Boolean alternatives. Unanchored custom values conservatively retain
all target contexts. Use `.At(value => owner)` when such values have a source owner.
Callbacks and custom selectors used during automatic planning must read source
facts only. When a custom callback reads coverage directly, explicitly choose
contexts without evaluating that callback during planning:

```csharp
rules.For(customCoverageDependentSelection)
    .CollectCoverageIn(Code.Projects.Where(project => project.Name == "Product.Data"))
    .Forbid("DATA002", "Inspect the selected execution.");
```

This scope always collects its selected contexts. It must use source-only filters
and cannot contain a built-in coverage dependency. Operational collection failures
still fail the check. Method and type line scopes measure their full selected
declaration, while the diagnostic remains anchored to its identifier.

Discovery searches below the closest enclosing solution or repository root,
falling back to the target project directory. MSBuild evaluates test classification
(including packages awaiting restore),
frameworks, and transitive project references; only tests referencing the selected
project are run. Both VSTest and Microsoft.Testing.Platform workflows are supported.
Tests outside that root are not discovered. Loose source has no reproducible test
project identity and produces unknown coverage.

## Occurrence evidence

Findings retain the selected occurrence's physical source location and append
`[coverage: uncovered]` or `[coverage: unknown (reason-code)]`. A covered occurrence satisfies
the requirement. Source byte checksums and portable PDB identities must match the
captured compilation, including compiler options and all source documents, and the
selected project/framework/build output. Associated external or embedded portable
symbols are supported. Evidence from
another assembly build, missing symbols, exclusions, ambiguous assembly names,
or changed source cannot satisfy it. Generated sources are outside the ordinary
rule scope.

The integration reads instrumented ranges with both line and column coordinates;
it does not equate a covered line with an executed call. A range hit identifies an
exact expression or a direct expression in an expression statement, return, throw,
or single-variable initializer, including parentheses, direct awaits, and checked
expressions. Broader ranges and partially
covered ranges are conservative. For example:

```csharp
var result = cached ?? await store.LoadAsync();
```

Running the cached path cannot establish that `LoadAsync` ran. When sequence-point
and block evidence cannot distinguish the selected expression, the result stays
unknown. This version does not rewrite consumer source or introduce extra probes
inside expressions. Unknown evidence is a failing requirement, so it can produce
findings even when a test actually exercised the expression. Use
`Coverage.Executed.ExecutionOf(occurrence)` to inspect prepared occurrence evidence.
Direct `RuleSet.Evaluate` and in-memory snippets without collected facts also
return unknown rather than launching tests synchronously.

## Line percentages and reuse

A line counts as fully covered only when all matching ranges are covered. Partial
lines count in the denominator but not the numerator. Binary block evidence is
merged across successful test runs before exporting source ranges and counting
distinct lines within each document and framework context. Complementary branch
hits can therefore cover a line collectively. Project totals sum covered and
coverable counts instead
of averaging file percentages. Missing document evidence and zero coverable lines
fail even a zero-percent requirement. Collector exclusions therefore never grant
vacuous success; explicitly filter files outside your policy's scope.

Threshold findings include the percentage and counts, for example
`[line coverage: 66.67% (2/3), required 90%]`. Framework contexts remain separate.
Only modules matching the target's portable PDB identity contribute; tests can
reference a library using a different compatible target framework.

Successful reports are shared across rules and matching test runs within an
analysis. Persistent reuse keys include collector version, environment, global
build overrides, repository source and test inputs, metadata dependencies, and
build outputs, discovered imported and declared inputs, and SDK versions. Applicable
tests are restored and imports re-evaluated before capturing collection inputs.
MSBuild discovery runs on each check to validate inputs without rerunning tests.
Within an analysis, root inventories, file digests, and build queries are shared
across projects. Restore and test execution discard those memoized inputs before
fresh validation; timestamp-preserving changes during tests are still detected.
Changes invalidate the report; incomplete and failed runs are
never published to the cache. Publication is atomic and concurrent checks share
an exclusive collection lock per source root; independent roots can run concurrently.
Tool installation has its own shared lock. Ordinary cache hits still check the
build's symbol identity before using evidence.

Files under `.git`, `.vs`, `artifacts`, and `TestResults` are outside the input
inventory; `bin` and `obj` contribute assembly, symbol and JSON build identities.
Tests depending on changing services, clocks or other external state must request
fresh evidence with `--refresh-coverage`, or `AnalysisOptions.RefreshCoverage` for
library use. The cache cannot fingerprint those external dependencies.

## Inspecting evidence

`Coverage.Executed.Inspect(occurrence)` returns read-only `CoverageEvidence` with
state, typed reasons, project/framework/context identity, matching source ranges,
and whether refreshing might help. `Coverage.Line.Measure(scope)` exposes covered
and coverable counts, completeness, reasons and percentage independently of a
threshold. SDK inspection reads prepared facts and never starts tests.

Unknown findings include compact reason codes such as `no-tests`, `symbol-mismatch`,
`partial-range` and `unsupported-mapping`. A missing report document is described
as missing or possibly excluded; the integration does not invent an exclusion it
cannot verify. Line findings retain counts even when incomplete or zero-coverable.
`CoverageEvidenceFormatter.Explain(reason)` supplies actionable explanation text.
Unsupported mappings explicitly explain that adding tests alone may not fix them.

Pass `--explain-coverage` to include context identity, typed explanations and
matching ranges, or set `AnalysisOptions.ExplainCoverage` for library/bundle use.
Ordinary bundle output omits ranges; typed state/reasons/counts remain available
through diagnostics, validated findings and cross-context aggregation. Response
protocol 6 requires matching CLI/SDK packages and rebuilt bundles. Collection,
build, tool and test failures still stop the check rather than becoming unknown
findings.

## Synthetic rule-policy fixtures

`DrillPress.Testing` can supply source-bound synthetic evidence without installing
tools, discovering projects, or running tests:

```csharp
var workspace = new RuleTestWorkspace();
var project = workspace.AddProject("Product", [new TestSource("Queries.cs", source)]);
workspace.WithCoverage(facts => facts
    .ForCall(project, "Queries.cs", "store.LoadAsync()")
    .Executed()
    .ForFile(project, "Queries.cs")
    .Lines(8, 10));
var result = await workspace.CheckAsync(rules);
```

Choose `Executed()`, `NotExecuted()`, or `Unknown(reason, ...)` explicitly. Unknown
requires defined reasons. `Lines(covered, coverable, isComplete, reasons)` keeps
counts and completeness separate; incomplete and zero-coverable evidence fail
even a zero threshold. File measurements also contribute to project totals.

Text-only selectors must identify one resolved source call or document across the
workspace. Use the project overload to identify the exact framework membership,
then an explicit zero-based `occurrenceIndex` for repeated call text. `ForCall(call)`
also accepts a bound call from this workspace. Missing, ambiguous, generated, and
invalid call selectors reject rather than silently choosing a match. Facts bind
immediately, reject duplicates, and are reapplied to fresh analysis lifetimes;
changed source identity rejects before evaluation. Unspecified facts remain unknown.

`TestFinding.Evidence` and `TestFinding.Coverage` retain validated readable and
typed evidence from every contributing context. This fixture uses the production
requirement evaluator, response construction, and validator. Synthetic facts are
restricted to the testing package's fixture path; production checks still require
collector/build/source identity validation. Keep real collector integration tests
separate from these deterministic rule-policy tests.

## Coverage outcome policy

Strict execution and line requirements fail for uncovered and unknown evidence.
Declare different remediation without rebuilding that state machine:

```csharp
rules.For(queryExecutions).Require(
    Coverage.Executed
        .OnUncovered("Exercise this query in tests.")
        .OnUnknown("Inspect the coverage explanation."),
    "DATA002", "Queries require verified test execution.");
```

The registered rule description stays stable; `OutcomeRemediation` describes each
occurrence. To make an unsupported execution mapping a visible review item,
explicitly opt into `.ReviewUnknownFor(CoverageReason.UnsupportedExpressionMapping)`.
Every unknown reason must be eligible. Missing tests/symbols, identity mismatches,
exclusions, partial ranges, and collection/build/test/tool failures cannot become
review-only findings. Empty or ineligible review configuration rejects.

Review is a gating choice: `Disposition` remains visible, the CLI adds `[review]`,
and a check containing only review findings exits 0. Execution remains `Unknown`,
`SatisfiesRequirement` stays false, and no covered claim is made. Any violation in
another contributing context retains failing exit 1. Composed conditions retain
automatic collection; any failing non-review condition retains violation gating.
Negated conditions retain ordinary Boolean semantics and default violation gating.

`CoverageEvidence.ReviewReasons` carries the explicit policy independently of the
measured state; `MinimumPercentage` carries a line requirement's threshold. Bundle
validation checks eligible reasons, measured counts, thresholds, and review gating
before accepting a successful exit. Response protocol 6 requires matching packages
and rebuilt bundles.
