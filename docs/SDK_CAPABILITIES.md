# Composable .NET rule SDK

The authoring surface covers C# source and evaluated .NET project facts. Rules
are ordinary compiled C#; the SDK supplies reusable selections and evidence.
Repository policy stays in the consumer bundle.

## Where changes belong

The `DrillPress.RuleAuthoring` project is organized by responsibility. Except
for the small `DrillPress` rule/query facade, folder names match namespaces.

| Folder / namespace | Responsibility |
| --- | --- |
| `Rules` / `DrillPress` | Registration, Boolean conditions, diagnostics and candidate contracts |
| `Queries` | Lazy query composition, source files, syntax nodes and anchored custom results; `CodeQuery` and predicate extensions remain in `DrillPress` |
| `Analysis` | Solution, project, source, method and named-type views |
| `Semantics` | Type/member identities, attributes, declarations and references |
| `Operations` | Compiler operations, invocations and argument mapping |
| `Flow` | Method-local CFG, data flow and nullable state |
| `Relationships` | Source inheritance, declaration ownership and bound invocation paths |
| `Projects` | Evaluated project roles, packages and compatible source graphs |
| `Facts` | Consumer facts cached per solution or evaluated project |
| `Collections` | Inventory comparison and repeated token shapes |
| `Configuration` | Immutable API sets and path patterns |
| `Baselines` | Accepted .NET source state and symbol comparisons |
| `Fixes` | Edit construction, contextual compiler proofs and specialized fixes |
| `Testing` | Semantic xUnit selection and test-body analysis |

`DrillPress.Testing` is a separate consumer test-kit project using the
`DrillPress.Testing` namespace. It references the production evaluator and
validator. Test projects mirror the production folders. This is a source-level
SDK change: earlier bundles must update imports for types moved out of the
root namespace. XML documentation is generated for authoring and testing APIs.

## Reading and writing rules

```csharp
using DrillPress;
using DrillPress.Configuration;
using DrillPress.Operations;
using DrillPress.Queries;
using DrillPress.Semantics;

var rules = new RuleSet();
var adapters = new PathPattern("**/Tracing/*.cs");
var consoleOutput = CodeType.Named("System.Console").Member("WriteLine");

rules.For(OperationQueries.Invocations.Where(call => call.Calls(consoleOutput)))
    .Require(call => adapters.Matches(call.Source.Document.Path),
        "TEAM001", "Keep console output in the Tracing adapter.");
```

`Forbid` reports selected candidates. `Require` reports candidates failing its
predicate. Both accept exact location selectors and optional fix factories.
`Where` and `ExceptWhen` accept lambdas or reusable `RuleCondition<T>` objects.
Named conditions retain `And`, `Or`, `Not` and short-circuit evaluation.

Use `files.Invocations()`, `files.Declarations()` or `files.Nodes<TSyntax>()`
when a policy applies to a small file scope. This avoids binding unrelated
projects. Paths are case-sensitive; `PathPattern` matches the entire supplied
path, normalizes slashes, and supports `*`, `?`, `**` and optional directories
with `**/`. It does not guess a repository root or access the filesystem.

`Sources.Files`, `Sources.Projects`, `Sources.Nodes<TSyntax>()`,
`Sources.Attributes`, `SymbolQueries.Declarations`, `SymbolQueries.References`,
`OperationQueries.Of<TOperation>()` and the existing `Code` roots share one
analysis. Declarations include fields, properties, events, parameters, locals
and partial occurrences. References select resolved simple names; implicit
uses belong to operation queries. `Code.Types` continues to deduplicate partial
named types within each evaluated context.

`CodeNode` exposes its original syntax, compiler operation, constant and nullable
type information. `CodeInvocation` exposes the chosen overload, receiver and
`Argument("parameterName")`, including named arguments and implicit defaults.
`CodeType.Member(name)` creates a member on that declaring type. Its `References`
query selects fields, properties or method references, including aliases and
static imports, while retaining the engine's name-based discovery optimization.
For example, `CodeType.Of<string>().Member(nameof(string.Empty)).References`
is ready to pass to `rules.For(...)`.

`member.WithParameters(CodeType.Of<string>())` selects an exact method overload;
`member.WithParameters()` selects only the parameterless overload. Omitting
parameter selection matches all overloads. The original descriptor is unchanged.
The constructor and `type.Member(name, parameters)` remain available. `CodeType`
supports assembly qualification, constructed generic identities and arrays.
For open generics, prefer `CodeType.Named("System.Buffers.ArrayPool<>")` or
`CodeType.Named("System.Collections.Generic.Dictionary<,>")`. Empty slots
normalize to CLR metadata arity (`ArrayPool` followed by a backtick and `1`,
for example); existing metadata spellings remain supported. Whitespace between
slots is allowed. After an open generic type, dots select nested types, as in
`Outer<>.Inner<,>`. Existing `+` separators still work; use `+` when a non-generic
containing type would otherwise be indistinguishable from a namespace.
Array suffixes are preserved. These names match any constructed arguments;
use `CodeType.Of<List<string>>()` when arguments must match exactly. Named
arguments such as `List<T>` or `List<string>` are not parsed by `Named`.
Raw Roslyn symbols, compilations and semantic models remain deliberate escape
hatches for policies requiring compiler detail.

## Custom roots, facts and requirements

```csharp
var sleep = CodeType.Named("System.Threading.Thread").Member("Sleep");
var blockingAsyncMethods = Code.Methods.Where(method => method.IsAsync && method.Reaches(sleep));

rules.For(blockingAsyncMethods)
    .Forbid("TEAM002", "Keep blocking sleeps out of asynchronous call paths.");
```

Start from the object whose information you need: `method.Flow` gives its cached
compiler flow facts; `method.Reaches(member)` follows statically bound source
calls; `method.Solution.Relationships` and `solution.ProjectGraph` expose shared
solution-wide analysis. These are entry points into the existing analysis, not
separate caches or alternate matching semantics.

`CodeQuery<T>.Create` is the public extension point. A query can `Select`,
`SelectMany`, `Join` or `WithoutMatching` another query and can be read with
`In(solution)`. Each query instance has one materialized result per analysis;
custom roots are shared across derived selections. Enumeration observes
cancellation. Keep selectors and conditions pure, and retain the query instance
when sharing it across rules.

`AnalysisFact<T>` caches a consumer value for the solution. `ProjectFact<T>`
computes separately and lazily for each evaluated project. Facts can read
queries and other facts; fact cycles fail through lazy initialization. Failed
computations remain failed for that analysis. Values should be immutable. A
new `AnalysisSolution` creates a fresh cache lifetime; no cache is persisted
globally or across source changes. The production evaluator runs rules
sequentially; arbitrary parallel access to all Roslyn wrappers and relationship
indexes is not a supported execution model.

```csharp
var codecs = Code.Types.Where(type => type.Implements(codecContract));
var roundTrips = Code.Methods.Where(method => method.Source.Project.IsTestProject);

rules.For(codecs.WithoutMatching(roundTrips,
        codec => (codec.Name + "RoundTrip", codec.Source.Project.TargetFramework),
        method => (method.Name, method.Source.Project.TargetFramework)))
    .Forbid("TEAM003", "Provide a round-trip example for each codec.");
```

Keys are explicit policy: include framework, namespace, ownership or project
identity as required. For dependency-sensitive matching, use the predicate
overload and `solution.ProjectGraph.Includes(consumer, owner)`, as the complete showcase
does. `CompatibleViewsOf` retains separate alternative evaluations. A missing
counterpart is reported on its existing owner. Use `.At(result => result.Owner)`
to anchor a custom fact or joined tuple. Project-only requirements likewise need
an existing ordinary source anchor; diagnostics on absent files or empty
projects are not represented by the current source diagnostic protocol.

## Analysis boundaries

| Capability | Implemented contract | Deliberate boundary |
| --- | --- | --- |
| Syntax and trivia | Original Roslyn syntax/text, paths, modifiers, comments, generated classification, precise spans; xUnit blank-line helpers | No universal primary-type or human-quality judgment |
| Semantics | Resolved identities, attributes, generic definitions, conversions, constants, overloads and nullable state | Ambiguous bindings are never guessed |
| Operations and flow | Cached operation roots, compiler CFG/data-flow sets, nullable flow at an expression | No interprocedural alias, transaction, token-provenance or general behavioral-equivalence engine |
| Relationships | Inheritance, source ownership, references, callers and bound invocation paths, generated call bodies included | Paths follow invocation targets; constructor/property/event edges, dynamic dispatch, reflection, delegate dispatch (including lambda bodies) and DI resolution are not inferred |
| Project facts | Context identity, test classification, source edges, TFM, nullable/compiler settings, direct packages including central versions, source-root items and selected policy properties | No transitive package inventory or indiscriminate export of environment/MSBuild properties |
| Baselines | Added/modified source, previous accessibility, changed declarations including partials, using accepted analyses | No Git integration, rename detection, migration deployment state or baseline CLI transport |
| Configuration | Typed C# records/closures, `CodeType`, `CodeMember`, `ApiSet`, `PathPattern` and query exceptions | No configuration DSL or automatic suppression database |
| Sets and duplicates | Keyed comparisons and exact repeated token shapes ignoring trivia | No semantic clone detector; repeated syntax does not prove equivalent behavior |
| Requirements | Joins, contextual absence checks and source-anchored missing counterparts | Consumers supply ownership and naming policy |
| Facts | Lazy values per analysis or evaluated project, composable with built-in queries | Cache identity is the fact instance; values are not serialized |
| Fixes | Multi-file text batches, caller-supplied semantic proof, compiler checks, binding helpers, conservative modifier and existing expression/argument fixes | No file create/delete/move or solution-wide rename/inlining refactoring engine |
| Consumer tests | Explicit references or runtime defaults, multiple projects/contexts, generated source, exact findings, fixed text and withheld fixes | Framework labels do not select reference packs; no OS file writes or target builds |

Generated sources contribute compiler semantics and can be inspected through
`Sources.FilesIncludingGenerated`; reporting candidates normally exclude them.
The evaluator also suppresses custom candidates anchored to generated source.
Linked files remain separate memberships until final physical aggregation.
Alternate frameworks use separate compilations, symbols and caches. Compare
symbols through their actual evaluated source graph rather than unqualified
names. Error syntax remains available, but semantic values may be absent or
invalid. `MethodFlow.Data.Succeeded` must be checked; declarations without bodies
have no executable CFG. A negative call-path result does **not** establish that
runtime execution cannot reach the API.

## Safe corrections

`SourceChanges.Replace` constructs an exact original-text/fingerprint edit.
`SourceChanges.Propose` takes a complete batch and a required
`Func<RewriteContext, bool>` proof. It checks source eligibility, inactive text,
conflicts within the batch and compiler errors before calling the proof with
all local edits applied to the affected compilation. It conservatively withholds
fixes when that compilation has errors. The callback must establish the intended
semantic correction; successful compilation alone is insufficient.

`BindingProof.PreservesEnclosingExpressions` reuses the existing enclosing
expression/symbol/conversion check, excluding `nameof` and expression trees.
It does not prove side effects, disposal, ref behavior or evaluation order.
`ModifierFix.RemoveRedundantAccessibility` checks the compiler's accessibility
before and after the edit in every affected context. The existing
`EmptyStringFix` and allowlisted `OrdinalComparerFix` retain their contracts.

The engine validates every loaded context containing edited physical files,
including contexts that reported no violation. Its response validator withholds
conflicting or disagreeing batches in full. A multi-file *proposal* is a complete
unit for validation; OS application is still atomic **per file**, with the
existing recovery report if a later file fails. See [fixing](FIXING.md).

## Consumer tests and examples

```csharp
using DrillPress.Testing;

var workspace = new RuleTestWorkspace();
workspace.AddProject("Example", [new TestSource("Example.cs", sourceText)]);
var result = await workspace.CheckAsync(rules, cancellationToken);

Assert.Equal(expectedFindings, result.Findings);
Assert.Equal(expectedSource, result.FixedText("Example.cs"));
```

`TestFinding` contains rule ID, path, exact line/column/text and `HasFix`.
The test kit runs the production evaluator and validator, so withheld fixes are
tested through the same path as normal checks. `FixedText` applies the validated
plan in memory; create a new workspace from that text to assert a clean recheck.
Default references are read once per workspace from the runtime assembly
inventory through the filesystem abstraction. Supply metadata references to the
constructor, or per project, for synthetic fixtures and reference-pack fidelity.
Use the same source path and text in multiple projects to test linked files;
use separate symbols/reference sets to exercise alternate evaluations.

The [codec showcase](../samples/DrillPress.SampleRules/ShowcaseRules.cs) supplies
thirteen independent policies covering the capability groups above. The
[runnable target](../samples/CodecExamples/README.md) demonstrates useful findings
and an eligible correction. Consumer tests exercise the remaining policies with
focused snippets, including packages and accepted source baselines.

`ShowcaseRules` is the policy list: named predicates and selections express intent,
while the [CodecPolicies helpers](../samples/DrillPress.SampleRules/CodecPolicies)
contain the implementation. Change `CodecSources` for project/file scope and
declaration conventions, `CodecBehavior` for calls and flow analysis, and
`CodecExamples` for counterpart matching, repeated examples, and format inventories.
These helpers compose the existing SDK; they are sample-specific vocabulary, not
another rule engine or a new public SDK API. For example:

```csharp
rules.For(code.Calls.Where(CodecBehavior.WritesToConsole))
    .Require(CodecBehavior.IsInTracingAdapter,
        "SDK2002", "Keep console output in the Tracing adapter.");

rules.For(code.TextCodecs.WithoutMatching(examples.RoundTripTests, examples.IsRoundTripTestFor))
    .Forbid("SDK2004", "Provide an xUnit <CodecName>RoundTrip test for each text codec.");
```

The showcase's production call policies exclude test setup. Round-trip coverage
requires genuine xUnit Fact/Theory methods for concrete production codecs in a compatible test project, using
the explicit `<CodecName>RoundTrip` naming convention; this checks discoverable
coverage, not whether the assertions prove correctness. The buffer policy flags
captured locals initialized directly by `ArrayPool<T>.Rent`, independently of
variable names. It deliberately prohibits all such captures, even callbacks
invoked before `Return`; it does not infer aliases, escaping delegates or lease
lifetimes. The runnable `PooledUtf8Decoder` shows why that ownership convention
is useful. Inventory and duplicate-example policies remain explicit team
conventions, not general claims that all duplication is harmful.
