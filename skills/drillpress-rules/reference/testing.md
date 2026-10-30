# Testing rules and fixes

Reference `DrillPress.Testing` (same exact version as the SDK) from the test project.
`RuleTestWorkspace` builds in-memory projects and runs the production evaluator and
fix validator; nothing is written to disk.

## Workspace

```csharp
using DrillPress.Testing;

var workspace = new RuleTestWorkspace();
var domain = workspace.AddProject("Shop.Domain", [
    new TestSource("Order.cs", "public class Order { }"),
]);
workspace.AddProject("Shop.Tests", [
    new TestSource("OrderTests.cs", "class OrderTests { Order value = new(); }"),
], isTest: true, dependencies: [domain]);
```

`AddProject(name, sources, ...)` options:

| Parameter | Use |
| --- | --- |
| `isTest` | classify as a test project (`InTestProjects()`, `IsTestProject`) |
| `dependencies` | earlier projects to reference |
| `framework` | context label such as `net8.0` (does not change references) |
| `symbols` | preprocessor symbols for `#if` |
| `allowErrors` | keep source that does not compile (otherwise `AddProject` throws with the errors) |
| `references` | explicit `MetadataReference`s instead of the default |
| `packages` | direct package facts for `ReferencesPackage` (nothing is downloaded) |
| `nullable`, `languageVersion` | compiler options; defaults are enabled and C# 14 |
| `implicitUsings` | `true` adds the .NET SDK default global usings (`System`, `System.Linq`, `System.Threading.Tasks`, ...), like a project with `ImplicitUsings` enabled; off by default |
| `outputKind` | `OutputKind.ConsoleApplication` for top-level statements; a class library by default |

`new TestSource(path, text, Generated: true)` marks generated code. The default
references are the test process's own assemblies, so snippets can use the BCL and
packages the test project references, such as test-framework attributes for rules
about `Code.TestMethods`. A project named like a loaded assembly, such as `Shop.Core`,
keeps that assembly, so its snippets can use the real `Shop.Core` types; projects that
depend on it see the workspace project instead of the assembly. Use the same path
and text in two projects to model a linked file.

## Assertions

- `await workspace.CheckAsync(rules, cancellationToken)` returns `RuleTestResult`.
- `result.Output` is the exact CLI text. Assert it whole with a raw string ending in an
  empty line and `.ReplaceLineEndings("\n")`. Clean results are `""`.
  Format: `ID message`, then each file, then each location indented two spaces as
  `line:column` (column omitted when 1). A validated fix puts `+` directly before the
  line number, with no space: `  +5:32`, not `+ 5:32`. Coverage findings append
  `[coverage: state]`.
- `result.FixedText(path)` applies validated fixes in memory; assert the complete text.
- `result.ExplainedOutput` is `Output` plus a `    fix withheld: <reason>` line under each
  finding with a recorded rejection, naming the check that rejected it. A factory
  returning null supplies no explanation. Use it to
  debug a missing `+`, and to assert why a fix is withheld.
- `result.Findings` gives `TestFinding(Rule, Path, Line, Column, Text, HasFix)` when the
  highlighted text matters.
- `workspace.Analyze()` returns an `AnalysisSolution`; `query.In(solution)` materializes
  a query for focused tests of custom selections.

Test the allowed neighbor of every violation (other overloads, `nameof`, test projects,
configured exceptions) and, for fixes, a case where the proof fails.

## Allowances

Code accepts findings in a bounded source scope with
`// drillpress-allow RULE-ID[, RULE-ID]: reason`.
After code it allows findings that start on its line; on its own line it allows the
whole statement or member/type declaration starting on the next code line (only
comment lines may come between, not blank lines, `#if` directives or inactive code), or just that line when no statement or declaration
starts there. IDs match the catalog exactly; only ordinary single-line comments count,
and matching uses the finding's reported start (including `ReportAt`). `CheckAsync`
applies allowances like the CLI, so a test can show that a rule's legitimate
exceptions are expressible:

```csharp
using DrillPress;
using DrillPress.Testing;
using MyRules;
using Xunit;

public sealed class LoggingAllowanceTests
{
    [Fact]
    public async Task An_explained_allowance_accepts_one_console_call()
    {
        var workspace = new RuleTestWorkspace();
        workspace.AddProject("App", [new TestSource("App.cs", """
            class Banner
            {
                void Show()
                {
                    // drillpress-allow TEAM001: the host writes its banner before logging starts
                    Console.WriteLine("Shop");
                    Console.WriteLine("ready");
                }
            }
            """)], implicitUsings: true);

        var result = await workspace.CheckAsync(ExampleRules.Create(),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            TEAM001 Use the application logger instead of Console.WriteLine.
            App.cs
              7:17

            """.ReplaceLineEndings("\n"),
            result.Output);
    }
}
```

Allowances that allow nothing are reported as `drillpress-allow-unused` (the evidence
lists the unused rule IDs; the fix removes them or the comment, except over `#if`
directives or inactive code, where the evidence adds `another build configuration may
use it` and there is no fix), and allowances without a rule ID or reason, naming an
unknown rule or separated from their code by a blank line, directive or inactive code as
`drillpress-allow-invalid`. Invalid allowances suppress nothing. Overlapping allowances
use the smallest scope; a wider unused allowance is still reported. For linked files
and target frameworks, a rule ID is used when any context uses that physical comment;
skipped coverage rules are neither used nor unused. Rule IDs starting with
`drillpress-` (case-insensitively) are reserved and cannot themselves be allowed.

## Coverage requirements

Coverage rules read evidence the CLI collects by running tests. In unit tests, supply
synthetic evidence per occurrence:

```csharp
using DrillPress;
using DrillPress.Testing;
using Xunit;

public sealed class CommitCoverageTests
{
    [Fact]
    public async Task Each_commit_call_must_be_executed()
    {
        var workspace = new RuleTestWorkspace();
        var shop = workspace.AddProject("Shop", [new TestSource("Orders.cs",
            "class Orders\n{\n    void Commit() { }\n\n    void Save() => Commit();\n\n    void Retry() => Commit();\n}\n")]);
        workspace.WithCoverage(facts => facts
            .ForCall(shop, "Orders.cs", "Commit()", occurrenceIndex: 0).Executed()
            .ForCall(shop, "Orders.cs", "Commit()", occurrenceIndex: 1).NotExecuted());
        var rules = new RuleCatalog();
        rules.Rule("TEAM040", "Exercise each commit in tests.")
            .For(Code.Calls.ToMethodsNamed("Commit"))
            .Require(Coverage.Executed);

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            TEAM040 Exercise each commit in tests.
            Orders.cs
              7:21 [coverage: uncovered]

            """.ReplaceLineEndings("\n"),
            result.Output);
    }
}
```

`ForCall(path, text)` needs unique call text; otherwise pass the project and a zero-based
`occurrenceIndex`, or a selected `CodeInvocation`. `Unknown(reasons)` models missing
evidence, `ForEnumeration(loop).Started()` loop advancement, and
`ForFile(path).Lines(covered, coverable)` line thresholds.

## Running against real code

Build the rule bundle before running `drillpress check`, `fix` or `status` with
`--rules path/to/Rules.dll target`. Targets are solutions, projects, directories or
loose `.cs` files. The CLI loads the target with its SDK; the bundle consumes the
captured compilation. Source generators receive additional files in build order,
and referenced contexts generate before their dependents. Independent contexts can
load in parallel; failures are reported in snapshot order.

### Options and output

| Option | Effect |
| --- | --- |
| `--explain-fixes` | Under a finding with a recorded rejection, prints `    fix withheld: <reason>` (four spaces). No extra text for a fix factory returning null. |
| `--profile` | Emits JSON timing/counter events on stderr; compact diagnostic stdout is unchanged. |
| `--no-optimization` | Uses exhaustive discovery for comparisons; findings should agree. |
| `--include-referenced-projects` | Also lints dependencies of a project target. |
| `--property Name=Value` | Overrides an evaluated build property; repeat for several. |
| `--validate-compilation` | Validates the captured target compilation. |
| `--show-fix-complexity` | Adds assigned complexity to each rule header. |
| `--fix-complexity trivial,local,complex,architectural,unspecified` | Selects levels after full validation; a batch shared with an excluded rule is withheld. |
| `--explain-coverage` | Prints coverage contexts, reasons and source ranges. |
| `--refresh-coverage` / `--skip-coverage` | Refreshes cached evidence / skips coverage rules and lists them; mutually exclusive. |
| `--build-host path` | Selects an explicit BuildHost rather than the installed one. |
| `--parallel-queries` | Prepares the queries of different rules concurrently; see [faster checks](#faster-checks-on-large-solutions). |
| `--stream-compilation` | Compiles each project in the bundle as soon as capture finishes it; see [faster checks](#faster-checks-on-large-solutions). |

Findings use physical line/column coordinates. Calls report at their method name,
including the final method in a fluent chain; `ReportAt` can choose another span.
A validated fix is marked `  +5:32`, and column 1 is omitted (`  +5`). `fix`
applies only validated batches and reports remaining findings. `status` shows
project violation counts by fix complexity. Exit codes are 0 clean, 1 violations,
2 failure, including a rule exception or unfinished rule. Allowances suppress
findings before counting and fix planning; see [allowances](#allowances).

Withheld explanations can identify source-shape gates, compiler errors, failed
proofs, disagreements between contexts, conflicts or a complexity selection.
`RuleTestResult.ExplainedOutput` uses the same format. Library callers can use
`CompactDiagnosticRenderer.Render(result, explainFixes: true)` in `DrillPress.Manifest`.
Explanations diagnose a proposal; they do not grant permission to bypass its proof.

### Faster checks on large solutions

The defaults suit small targets. On a large solution, two bundle properties and two
CLI options shorten a full check, but on a small target they slow it down, so the
bundle template leaves them off. Time a full check of your own solution before and
after adopting them.

```xml
<PropertyGroup>
  <TieredCompilation>false</TieredCompilation>
  <ServerGarbageCollection>true</ServerGarbageCollection>
</PropertyGroup>
```

```sh
dotnet tool run drillpress check --parallel-queries --stream-compilation --rules MyRules/bin/Debug/net10.0/MyRules.dll path/to/App.sln
```

Seconds for `drillpress check` with DrillPress 0.0.3-preview.25, the bundle already built:

| Target | Cores | Defaults | Both properties | Properties and both options |
| --- | ---: | ---: | ---: | ---: |
| One small project, 8 rules | 16 | 5.4 | 6.6 | 9.5 |
| | 2 | 5.7 | 6.6 | 9.6 |
| One library and its project references, 8 rules | 16 | 22.1 | 18.8 | 14.9 |
| | 2 | 23.6 | 20.2 | 21.2 |
| A large solution, 14 compilation contexts, 125 rules | 16 | 53 | 37 | 22 |
| | 2 | 75 | 54 | 44 |

Findings were the same in every configuration.

- On the library, `TieredCompilation` alone gave all of the properties' gain (22.1 to
  18.6 s on 16 cores) and `ServerGarbageCollection` alone none (21.9 s). On the large
  solution, with tiering off and both options on, server GC moved a check from 26.3 to
  23.0 s on 16 cores and from 45.6 to 44.4 s on two.
- On two cores the options helped only the large solution.
- `--parallel-queries` prepares the queries of different rules at the same time, which runs
  your selectors, predicates, conditions and `AnalysisFact` and `ProjectFact` computations
  concurrently. All of them must be safe for concurrent calls as well as pure: for example,
  no unsynchronized cache shared between calls. Rule tests evaluate sequentially and do
  not catch a violation. Reporting and fixes stay ordered.
- `--stream-compilation` runs capture and the bundle at the same time, which raises peak
  memory. On the large solution with 16 cores, the combined peak of the check's processes
  was 1.05 GiB with defaults, about 1.3 GiB with the properties and about 1.6 GiB with
  streaming added. `--parallel-queries` added nothing measurable.

### Profiles

Each stderr line starts with `drillpress-profile ` followed by one JSON event with
the process role, PID, phase, wall/user/kernel CPU milliseconds and peak working set.
Counter events have a non-null `count` and zero durations. The CLI forwards profiling
to BuildHost and the bundle; their direct commands also accept `--profile`.

`rule.<id>` includes candidate selection and proposal construction, including shared
work the first rule triggered. `rule.<id>.own` excludes shared work. After evaluation,
`shared.<kind>` totals appear in name order for kinds that ran: `operations`,
`member-references`, `type-references`, `declarations`, `relationships`, `facts`,
`flow`, `errors`. Totals include coverage planning. Only outermost shared work in a
flow counts; materializing an individual query remains the rule's own work.

Use `member.symbol.bindings`, `type.symbol.bindings` and `operation.bound.documents`
to check discovery filters. Measurements are inclusive, process-local scopes, not
an additive breakdown; process CPU can exceed wall time with parallel work. Peak
working set is a process lifetime high-water mark. Profiling is best effort and
does not affect diagnostics; a failed probe/output sink disables further events.

### Bundle compatibility

Pin the tool and every SDK package to the same exact product version, and rebuild
the bundle after upgrading. The internal bundle response protocol is version **10**
(`BundleResponseProtocol.CurrentVersion`); an incompatible response is rejected.
`Finding.FixWithheld` transports a nullable rejection reason;
`AggregatedFinding.FixWithheld` exposes the aggregated reason. JSON omits null values.
The reason is absent for accepted fixes and when no proposal or reason was supplied.
Direct bundle JSON is an internal exchange format; consume the CLI's compact findings
when checking code.
