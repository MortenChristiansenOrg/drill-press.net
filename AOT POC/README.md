# Drill Press NativeAOT proof of concept

This proof of concept uses one solution-wide rule model for syntax, semantic,
project, and cross-project rules. Rule definitions are ordinary C# 14 code.
They are compiled directly into an executable rule bundle: there is no rule
file parser, expression-tree compilation, reflection-based discovery, or
dynamic managed assembly loading.

## Projects

- `DrillPress.Core` contains the composable rule API, Roslyn-backed solution
  model, target loader, deterministic diagnostics, and text-edit application.
- `DrillPress.SampleRules` defines seven rules and builds to an executable
  `DrillPress.SampleRules.dll`. The same project publishes to a native binary.
- `DrillPress.Cli` is a small NativeAOT coordinator. It starts either a managed
  executable rule DLL through `dotnet`, or a natively published rule bundle
  directly. It never loads rule code into its own process.

The sample target is in the repository's separate `Sample Solution` folder.

## Build and run

From the repository root:

```bash
dotnet build "AOT POC/DrillPress.Aot.slnx"

dotnet "AOT POC/src/DrillPress.Cli/bin/Debug/net10.0/drillpress.dll" \
  check \
  --rules "AOT POC/src/DrillPress.SampleRules/bin/Debug/net10.0/DrillPress.SampleRules.dll" \
  "Sample Solution/DrillPress.SampleTarget.slnx"
```

JSON Lines is the default output. A compact human format is also available:

```bash
dotnet "AOT POC/src/DrillPress.SampleRules/bin/Debug/net10.0/DrillPress.SampleRules.dll" \
  check "Sample Solution" --format text
```

Supported targets are `.sln`, `.slnx`, `.csproj`, `.cs`, a directory, or a
quoted `*`/`**`/`?` file pattern.

Exit codes are `0` for clean, `1` for findings, and `2` for invalid input or an
analysis failure. Diagnostics go to stdout; operational messages go to stderr.

## NativeAOT

Publish both executable components for a runtime identifier:

```bash
dotnet publish "AOT POC/src/DrillPress.Cli/DrillPress.Cli.csproj" \
  -c Release -r linux-x64 -o "AOT POC/artifacts/cli-linux-x64"

dotnet publish "AOT POC/src/DrillPress.SampleRules/DrillPress.SampleRules.csproj" \
  -c Release -r linux-x64 -o "AOT POC/artifacts/rules-linux-x64"
```

Then run the native coordinator and native rules:

```bash
"AOT POC/artifacts/cli-linux-x64/drillpress" \
  check \
  --rules "AOT POC/artifacts/rules-linux-x64/DrillPress.SampleRules" \
  "Sample Solution/DrillPress.SampleTarget.slnx"
```

Roslyn 5.0 has several missing trimming annotations in internal pooled-delegate,
UI-culture, assembly-location, and analyzer-loading paths. The rule bundle roots
the two Roslyn compiler assemblies and narrowly suppresses those warnings. This
POC does not invoke Roslyn's analyzer loader. Native publication and execution
against the sample solution are part of the acceptance check.

### Initial Linux x64 baseline

Measured in this workspace with a warm filesystem cache and eight findings:

| Artifact/run | Result |
| --- | ---: |
| Native coordinator (stripped) | 2.1 MB |
| Managed rule output including Roslyn dependencies | 16.8 MB |
| Native rule bundle (stripped, without `.dbg`) | 66.9 MB |
| Managed rule check, three runs | 0.81-0.90 s |
| Native rule check, three runs | 0.04 s each |

These are POC measurements rather than a formal benchmark, but they preserve a
useful size/startup baseline for comparison with the planned non-AOT design.

## Rule definitions and composition

Selections and conditions are reusable values:

```csharp
var xunitTests = Code.Methods.Where(Methods.HaveAnyAttribute(
    CodeType.Named("Xunit.FactAttribute"),
    CodeType.Named("Xunit.TheoryAttribute")));
var onlyAssertionIsThrows = Methods.HaveOnlyAssertion(
    Assertions.Are(CodeType.Named("Xunit.Assert"), "Throws"));

rules.For(xunitTests)
    .Require(
        id: "DP1001",
        condition: Methods.HaveAtMostEmptyLines(2),
        message: "Remove extra empty lines; keep at most two blank lines inside an xUnit test.",
        at: Methods.EmptyLineAfter(2))
    .Require(
        id: "DP1002",
        condition: Methods.HaveAllAssertionsAfterLastEmptyLine
            .ExceptWhen(onlyAssertionIsThrows),
        message: "Move every assertion after the final blank line in the xUnit test.",
        at: Methods.FirstAssertionBeforeLastEmptyLine);
```

`RuleCondition<T>` supports `And`, `Or`, `Not`, and `ExceptWhen`.
`ExceptWhen` makes the base requirement pass only when its exception condition
matches; exceptions are therefore ordinary reusable, composable conditions
rather than special cases inside the engine. `CodeQuery<T>.Where` can be layered
to create more specialized reusable queries. The complete definitions are in
`src/DrillPress.SampleRules/SampleRuleSet.cs`.

### Strongly typed type references

Type-sensitive helpers have generic overloads, so rule code gets compiler
checking, navigation, and rename support:

```csharp
Members.Are<string>(nameof(string.Empty))
Members.Are<StringComparer>(nameof(StringComparer.Ordinal))
Members.HaveType<DateTime>()

Methods.AreDeclaredOn<MyService>()
Methods.HaveAttribute<ObsoleteAttribute>()
Methods.Return<List<string>>()
Methods.HaveParameter<CancellationToken>()

Interfaces.Are<IMyContract>()
Types.Are<MyService>()
Types.Implement<IMyContract>()
```

`CodeType.Of<T>()` records the metadata name, exact closed generic arguments,
and—except for core-library facade types—the simple assembly name. Matching is
against Roslyn symbols rather than source spelling, so aliases and fully
qualified names behave identically. The identity is cached per closed `T`; this
uses `typeof(T)` metadata but does not scan assemblies or discover rule types.
A named generic identity with no supplied
arguments intentionally matches any construction; `ConstructedWith` can supply
arguments explicitly:

```csharp
var resultOfString = CodeType
    .Named("Acme.Contracts.Result`1", "Acme.Contracts")
    .ConstructedWith(CodeType.Of<string>());
```

#### Referencing types from another project

A rules project may directly reference a stable contracts/API project:

```xml
<ItemGroup>
  <ProjectReference Include="../Acme.Contracts/Acme.Contracts.csproj" />
</ItemGroup>
```

That enables expressions such as:

```csharp
Members.Are<LegacyApi>(nameof(LegacyApi.OldMethod))
Types.Implement<ICommandHandler>()
```

This is fully static and NativeAOT-compatible: the referenced assembly becomes
part of the rule bundle. It is a good fit for a small, stable contract assembly,
but referencing a large application project increases bundle size and couples
rule recompilation to that project. It must also not introduce a project cycle.

When a direct reference is undesirable or impossible, use a named identity:

```csharp
var commandHandler = CodeType.Named(
    "Acme.Contracts.ICommandHandler",
    "Acme.Contracts");

Code.Types.Where(Types.Implement(commandHandler));
```

Omit the assembly argument when the same metadata type name should match in any
assembly. This fallback loses compile-time rename checking but still performs
semantic matching in the target compilation; it is not runtime rule parsing.

The included rules are:

- DP1001: xUnit tests have at most two empty lines.
- DP1002: xUnit assertions occur after the final empty line, except that a sole
  `Assert.Throws` may occur before the second empty line.
- DP1003: an interface does not have exactly one concrete non-test
  implementation across the selected solution.
- DP1004: use `""` rather than `string.Empty` (fixable).
- DP1005: do not pass `StringComparer.Ordinal` (fixable when removing the
  argument still binds to a method).
- DP1006: use an injected `TimeProvider` rather than `DateTime.Now`.
- DP1007: use an asynchronous cancellable delay rather than `Thread.Sleep`.

## Applying fixes

```bash
dotnet "AOT POC/src/DrillPress.SampleRules/bin/Debug/net10.0/DrillPress.SampleRules.dll" \
  fix path/to/target.sln
```

Edits are grouped by file, checked for overlap, applied from the end of each
file, and written through a temporary file. The target is then analyzed again,
so stdout contains only remaining findings. DP1005 uses speculative semantic
binding and offers its removal only when the rewritten invocation resolves.

## Deliberate POC limitations

The AOT process does not host MSBuild. The small loader understands ordinary SDK
projects, project references, default `Compile` files, and the common SDK
implicit usings. It does not yet evaluate conditional MSBuild properties,
custom `Compile` items, NuGet compile assets, source generators, multi-targeting,
or per-project language settings.

A production version should invoke an SDK-side manifest broker which evaluates
MSBuild and writes source/reference/options manifests. The NativeAOT rule bundle
can consume those manifests without dynamically loading MSBuild or rule code.
