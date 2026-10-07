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

`new TestSource(path, text, Generated: true)` marks generated code. The default
references are the test process's own assemblies, so snippets can use the BCL and
packages the test project references, such as test-framework attributes for rules
about `Code.TestMethods`. Use the same path and text in two projects to model a
linked file.

## Assertions

- `await workspace.CheckAsync(rules, cancellationToken)` returns `RuleTestResult`.
- `result.Output` is the exact CLI text. Assert it whole with a raw string ending in an
  empty line and `.ReplaceLineEndings("\n")`. Clean results are `""`.
  Format: `ID message`, then each file, then `  line:column` (column omitted when 1);
  `+` before the line marks a validated fix; coverage findings append `[coverage: state]`.
- `result.FixedText(path)` applies validated fixes in memory; assert the complete text.
- `result.Findings` gives `TestFinding(Rule, Path, Line, Column, Text, HasFix)` when the
  highlighted text matters.
- `workspace.Analyze()` returns an `AnalysisSolution`; `query.In(solution)` materializes
  a query for focused tests of custom selections.

Test the allowed neighbor of every violation (other overloads, `nameof`, test projects,
configured exceptions) and, for fixes, a case where the proof fails.

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

Build the bundle and run the installed tool:

```sh
dotnet build MyRules
dotnet tool run drillpress check --rules MyRules/bin/Debug/net10.0/MyRules.dll App.sln
```

`--include-referenced-projects` also lints dependencies of a project target,
`--explain-coverage` shows coverage reasons, `--show-fix-complexity` prints each rule's
fix-effort estimate. A rule exception or an unfinished rule fails the run with exit code 2.
