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
requirements. The first coverage check automatically installs the pinned Microsoft
coverage tool into Drill Press's user-local cache. It requires an SDK, NuGet
access on first use, and working test projects; rule authors do not add collector
packages or configure report formats. SDK source snapshots must already be
restored as usual. Test runs build their projects and dependencies. A failed
installation, evaluation, build, test run or collection fails the check explicitly.
Cancellation stops external processes and removes temporary reports.

Discovery searches below the closest enclosing solution or repository root,
falling back to the target project directory. MSBuild evaluates test classification
(including packages awaiting restore),
frameworks, and transitive project references; only tests referencing the selected
project are run. Both VSTest and Microsoft.Testing.Platform workflows are supported.
Tests outside that root are not discovered. Loose source has no reproducible test
project identity and produces unknown coverage.

## Occurrence evidence

Findings retain the selected occurrence's physical source location and append
`[coverage: uncovered]` or `[coverage: unknown]`. A covered occurrence satisfies
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
