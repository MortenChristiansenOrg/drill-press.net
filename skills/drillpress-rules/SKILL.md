---
name: drillpress-rules
description: Writes, changes, debugs and tests custom DrillPress lint rules and safe automatic fixes in C#. Use when the user asks to add or modify a DrillPress rule, rule bundle, convention check, finding location or code fix, or mentions RuleCatalog, Code queries, Forbid/Require, Fix.For or RuleTestWorkspace.
---

# DrillPress rules

This skill matches DrillPress {{version}}. Reference every DrillPress package and the
`drillpress` tool at exactly this version, and rebuild the bundle after upgrading.

A rule is ordinary C#: name the convention, select the code it governs, state the
obligation, and optionally attach a fix that proves itself safe.

```csharp
using DrillPress;

var rules = new RuleCatalog();
var writeLine = CodeType.Named("System.Console").Member("WriteLine");

rules.Rule("TEAM001", "Use the application logger instead of Console.WriteLine.")
    .For(Code.Calls.To(writeLine).InNonTestProjects())
    .Forbid();
```

## Workflow

1. Find the bundle (the console project calling `RuleApplication.RunAsync`) and its
   test project. Create them from [Setup](#setup) when missing.
2. Phrase the convention as candidates + obligation + a message saying what to do instead.
3. Write the test first: one violation and a nearby allowed case; assert the
   complete `result.Output`.
4. Register the rule. Identify APIs by compiler identity (`CodeType`, `CodeMember`,
   `To`), never by name text, so aliases, `using static` and overloads are covered.
5. Add a fix only when it can be proven safe. Test `FixedText` and a withheld case.
6. Run the tests, then `drillpress check` against a real target.

## Rule shape

`rules.Rule(id, message[, fixComplexity])` → `.For(query)` → optional `.ReportAt(...)`
or `.ReportOncePer(key)` → `.Forbid(fix?)` or `.Require(condition, fix?)`.

- `Forbid()` reports every selected candidate; `Require(condition)` reports those
  failing it. Put the scope in the query and the obligation in `Require`.
- IDs are unique, messages one line. A rule without `Forbid`/`Require` fails evaluation.
- `Forbid`/`Require` return the rule, so another `.For(...)` adds a clause with the
  same ID, for example one for calls and one for loops.
- Conditions are lambdas or reusable `RuleCondition<T>` values (`And`, `Or`, `Not`,
  `ExceptWhen`). Keep them pure; reuse query instances to share work.

## Choose candidates

Start from the `Code` root that names what the convention is about:

| Root | Candidate |
| --- | --- |
| `Files`, `Projects` | `CodeFile`, `AnalysisProject` |
| `Types`, `Interfaces`; `TypeDeclarations` (each partial part) | `CodeTypeDefinition`; `CodeTypeDeclaration` |
| `Methods`, `Fields`, `Properties`, `Parameters`; `Declarations` (all other symbols) | `CodeMethod`, `CodeField`, `CodeProperty`, `CodeParameter`; `CodeSymbol` |
| `TestMethods`, `TestClasses` (xUnit, NUnit, MSTest) | `CodeMethod`, `CodeTypeDefinition` |
| `Calls`, `ObjectCreations` | `CodeInvocation`, `CodeObjectCreation` |
| `MemberReferences`, `TypeReferences`, `ReferencesTo(symbol)` | `MemberReference`, `CodeTypeReference`, `CodeSymbol` |
| `IfStatements`, `Catches`, `LocalVariables`, `ForEachLoops`, `OutVariables`, `Enumerations`, `NullChecks`, `Comments` | statement and comment shapes |
| `Nodes<TSyntax>()`, `Operations<TOperation>()` | any Roslyn syntax or operation |

Narrow with composable filters:

- Place: `InProject("Shop.*")`, `InTestProjects()`, `InNonTestProjects()`,
  `InNamespace("Shop.Domain.**")`, `InFolder("Domain")`, `InFilesNamed("*Tests.cs")`.
- Declarations: `Named(...)`, `NameMatching("*Service")`, `WithAttribute(type)`,
  `WithAccessibility(Accessibility.Public)`, `WithExplicitModifier(Modifier.Internal)`;
  tests `NameEndsWith`, `NameContainsWord`, `HasAttribute`, `HasDocumentationComment()`.
- Calls: `To(member or apiSet)`, `ToMethodsNamed`, `ToMethodsMatching("*Async")`,
  `OnReceiverOfType<T>()`, `WhereReceiver`, `WhereArgument("name", a => ...)`,
  `ArgumentsFor("name")` (argument candidates).
- Types and statements: `DerivedFrom`, `ImplementingInterface`, `ImplementationViews()`,
  `Branches().WithoutBraces()`, `WithExplicitType()`.
- General: `Where`, `ExceptWhen`, `Select`, `SelectMany`, `Concat`, `Join`,
  `WithoutMatching` (owners missing a counterpart).

`CodeType.Of<T>()` describes types the bundle can reference; `CodeType.Named("Ns.Type")`
(`"Ns.Cache<,>"` for open generics) describes the rest. `type.Member("Name")` matches
every overload; `.WithParameters(...)` selects one. Every root, filter and candidate
member is listed in [reference/queries.md](reference/queries.md).

## Report locations

Declarations report at their identifier, calls at the call, arguments at the argument,
catch clauses at their header, branches at the statement and projects at their first
file. `ReportAt` points elsewhere and is required for tuples and computed values:

```csharp
using DrillPress;

var rules = new RuleCatalog();

rules.Rule("ASYNC001", "Return Task instead of void from async methods.")
    .For(Code.Methods.Where(method => method.IsAsync && method.ReturnsVoid))
    .ReportAt(method => method.Syntax.ReturnType)
    .Forbid();

rules.Rule("PARAM001", "Group long parameter lists into a request type.")
    .For(Code.Methods
        .Select(method => (Method: method, Count: method.Parameters.Count))
        .Where(item => item.Count > 5))
    .ReportAt(item => item.Method)
    .Forbid();
```

`ReportAt` accepts a candidate part, syntax node, token (`type.ExplicitModifier(...)`)
or `SourceLocation`. `ReportOncePer(key)` shows one finding per key.

## Fixes

`Fix.For(candidate)` → an edit → `Propose()` when the library owns the proof, or
`SafeWhen(change => ...)` when you supply the policy-specific part. Return null to
keep the finding without a fix; never pass a proof that is always true.

```csharp
using DrillPress;

var rules = new RuleCatalog();
var empty = CodeType.Of<string>().Member(nameof(string.Empty));

rules.Rule("TEAM005", "Use \"\" instead of string.Empty.")
    .For(empty.References.OutsideNameOf())
    .Forbid(fix: reference => Fix.For(reference)
        .ReplaceWithLiteral("")
        .SafeWhen(change => change.Before.RefersTo(empty) && change.After.Is("")));

rules.Rule("TEAM006", "Add braces to if and else branches.")
    .For(Code.IfStatements.Branches().WithoutBraces())
    .Forbid(fix: branch => Fix.For(branch).AddBraces().Propose());
```

`Propose()` works for `AddBraces()`, `UseVar()`, comment `Remove()`, accessibility
`RemoveModifier(...)` and constant extraction. Expression replacements, argument
removal and other modifiers need `SafeWhen`. Builders already withhold fixes for
generated or inactive code, interior comments, compiler errors and changed bindings.
Details: [reference/fixes.md](reference/fixes.md).

## Tests

```csharp
using DrillPress;
using DrillPress.Testing;
using MyRules;
using Xunit;

public sealed class LoggingRuleTests
{
    [Fact]
    public async Task Console_output_is_reported_outside_test_projects()
    {
        var workspace = new RuleTestWorkspace();
        workspace.AddProject("App", [new TestSource("App.cs",
            "class Worker { void Run() => System.Console.WriteLine(1); }")]);

        var result = await workspace.CheckAsync(ExampleRules.Create(),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            TEAM001 Use the application logger instead of Console.WriteLine.
            App.cs
              1:30

            """.ReplaceLineEndings("\n"),
            result.Output);
    }
}
```

`Output` is exactly what the CLI prints: rule line, file, then `line:column`
(column omitted when 1), with `+` marking a validated fix. `FixedText(path)` applies
validated fixes in memory. `isTest: true`, `dependencies`, `allowErrors` and coverage
fakes are in [reference/testing.md](reference/testing.md).

## Setup

Bundle project (an executable):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DrillPress.Engine" Version="[{{version}}]" />
  </ItemGroup>
</Project>
```

```csharp
using DrillPress.Engine;
using MyRules;

return (int)await new RuleApplication().RunAsync(ExampleRules.Create(), args);
```

The test project references `DrillPress.Testing` at `[{{version}}]`, the bundle
project and the repository's test framework. Install the tool once per repository:
`dotnet new tool-manifest -o .config` and `dotnet tool install DrillPress.Cli --version {{version}}`.

## Run

```sh
dotnet build MyRules
dotnet tool run drillpress check --rules MyRules/bin/Debug/net10.0/MyRules.dll path/to/App.sln
dotnet tool run drillpress fix --rules MyRules/bin/Debug/net10.0/MyRules.dll path/to/App.sln
```

Targets can be a solution, project, directory or `.cs` file. Exit codes: 0 clean,
1 violations, 2 failure. `fix` applies only validated fixes and reports what remains.

## Pitfalls

- `MemberReferences` and `member.References` include `nameof(...)`; add `OutsideNameOf()`.
- Omitted optional arguments are bound (`Argument("x")`, `IsOmittedOr(value)`) but have
  no `Value`; findings on them report at the call. Filter `IsExplicit` when only written
  arguments matter.
- `Symbol` and `Type` can be null when code does not bind. Unknown facts are not violations.
- Generated files are excluded from roots and never anchor findings.
- An `else if` is not a missing brace; `WithoutBraces()` already skips it.
- `WithExplicitModifier` reads written tokens; `WithAccessibility` reads the compiler's
  effective accessibility, including defaults.
- Rules run per compilation context; a multi-targeted project is analyzed once per target.

## Bundled examples

- [examples/ExampleRules.cs](examples/ExampleRules.cs): eight rules covering calls,
  arguments, naming, documentation, a proven replacement fix, braces, modifiers and catches.
- [examples/ExampleRulesTests.cs](examples/ExampleRulesTests.cs): their tests, including
  scope, `nameof` exclusion and complete fixed text.
