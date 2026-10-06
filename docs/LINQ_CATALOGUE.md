# Optional standard LINQ catalogue

Add `DrillPress.Linq` at the same exact version as the SDK and import
`DrillPress.Presets`. The base SDK contains no LINQ policy or catalogue dependency.

## Select supported operations and handle classification gaps

Classify once and retain the original call through `At(...)`. Choose a source-only
consumer scope before classification; the example uses non-test projects where a
consumer-owned data-access type is available. Replace that type and scope with
your policy's context.

```csharp
using DrillPress;
using DrillPress.Presets;

var calls = Code.Calls.InNonTestProjects()
    .InProjectsWithType(CodeType.Named("Product.Data.DataAccess"));
var classified = calls
    .Select(call => (Call: call, Operation: StandardLinq.Inspect(call)))
    .At(item => item.Call);
var supportedTerminals = classified.Where(item =>
    item.Value.Operation.Status == LinqClassificationStatus.Supported
    && item.Value.Operation.Category is
        LinqOperationCategory.Scalar or LinqOperationCategory.Materializer);
var terminals = supportedTerminals.Select(item => item.Value.Call);
var sources = supportedTerminals.SelectMany(item =>
    item.Value.Operation.SequenceInputs.Select(input => input.Value));

var rules = new RuleSet();
rules.For(classified.Where(item =>
        item.Value.Operation.Status == LinqClassificationStatus.UnsupportedFramework))
    .Forbid("LINQ_FRAMEWORK", "Use a supported LINQ catalogue framework.");
rules.For(classified.Where(item =>
        item.Value.Operation.Status == LinqClassificationStatus.UnsupportedOperation))
    .Forbid("LINQ_OPERATION", "Add this exact LINQ overload to the maintained catalogue.");
```

Supported adapters and deferred construction are ordinary nonmatches. The two
unsupported rules retain the original source and project/framework membership and
fail the check with distinct remediation. To intentionally skip unsupported
classifications, omit those rules and document that choice in the consumer policy.
The supported terminal query remains usable with `terminals.Expressions()` and a
coverage requirement. Classification and its unsupported-result rules read no
coverage, so coverage collection remains limited to the selected supported candidates.

`NotStandardSymbol` is an ordinary nonmatch here: custom same-named APIs do not
become unsupported framework operations. `Unresolved` is a binding failure, not a
catalogue gap or unknown execution evidence. Use `--validate-compilation` when
exporting/checking the target to reject compiler errors explicitly:

```sh
drillpress check --rules Rules.dll Target.csproj --validate-compilation
```

Fast export otherwise permits ordinary compiler errors. Some invalid syntax has
no invocation operation at all, so operation filtering cannot replace compilation
validation. Unsupported classifications have no sequence-input facts; a scoped
classification finding cannot use those absent facts to establish query relevance
or claim that a query was uncovered.

## Declaration facts

`Inspect` matches signed framework declarations on `System.Linq.Enumerable` and
`System.Linq.Queryable`, normalizes reduced and constructed generic calls, then
looks up the exact overload and return signature. It exposes:

| Category | Examples | Meaning |
| --- | --- | --- |
| `Scalar` | `Any`, `Count`, `First`, `SequenceEqual`, `TryGetNonEnumeratedCount` | Scalar or selected-element operation; may use a collection fast path |
| `Materializer` | `ToList`, `ToArray`, `ToDictionary`, `ToLookup`, `ToHashSet` | Eager collection result |
| `Adapter` | `AsEnumerable`, `AsQueryable` | Changes the sequence view |
| `DeferredConstruction` | `Where`, `Select`, `OrderBy`, `Join`, `AggregateBy` | Deferred sequence construction or provider-facing query construction |
| `SequenceFactory` | `Empty`, `Range`, `Repeat`, `Sequence`, `InfiniteSequence` | Sequence creation without a sequence input; `Empty` may return a cached sequence immediately |

These categories describe declared operations. A scalar or materializer match is
not execution or enumeration evidence. Empty sources and collection fast paths
exist. `Queryable` identifies the provider-facing surface; it does not identify
the provider or prove when or how it evaluates the expression. There is no EF,
ORM async, wrapper, purity, reporting or instrumentation policy here.

`SequenceInputs` contains direct sequence parameters only, in written evaluation
order, with the original values and static types before contextual conversion.
It includes an extension receiver once in either spelling. For example,
`items.SequenceEqual(query)` exposes `first: items` and `second: query`, while
`Enumerable.SequenceEqual(second: query, first: items)` retains that reversed
source order. Queryable's equivalent parameter names are `source1` and `source2`.
`Zip` exposes all supplied sequence inputs. Comparers, callbacks, and sequences
produced by callbacks are not direct input roles.

The maintained framework tables are `net8.0`, `net9.0`, and `net10.0`, including
their platform suffixes. Framework labels come from the evaluated project context;
they do not change or invent compiler references. An overload introduced in a
later supported framework remains `UnsupportedOperation` in an older table.
Other frameworks, including future versions, return `UnsupportedFramework`.
New overloads absent from the table return `UnsupportedOperation`; custom APIs,
source lookalikes, ParallelEnumerable and AsyncEnumerable return
`NotStandardSymbol`. Calls with compiler errors return `Unresolved`. Unsupported
results have no category or sequence inputs. Consumer-owned families remain
expressible with `ApiSet` and `EvaluationInputs()`.

### Sequence consumption

`SequenceConsumption` describes reviewed behavior during the selected invocation,
separately from the result category. It describes the operation as a whole, not a
promise about any particular sequence-input role or executed call:

| Value | Maintained scope | Meaning |
| --- | --- | --- |
| `NeverEnumerates` | Supported `Enumerable.TryGetNonEnumeratedCount` overloads in .NET 8, 9 and 10 | The framework operation does not acquire or advance an input enumerator |
| `MayEnumerate` | Other supported `Enumerable` scalar operations and materializers | Can acquire or advance an input enumerator; collection fast paths and empty inputs remain possible |
| `Unknown` | `Queryable`, adapters, deferred construction, sequence factories, and every unsupported/nonstandard/unresolved classification | No maintained consumption claim |

For example, select potentially consuming `Enumerable` operations without a
method-name exception:

```csharp
var potentiallyConsuming = classified.Where(item =>
    item.Value.Operation.Status == LinqClassificationStatus.Supported
    && item.Value.Operation.SequenceConsumption == LinqSequenceConsumption.MayEnumerate);
var sequenceInputs = potentiallyConsuming.SelectMany(item =>
    item.Value.Operation.SequenceInputs.Select(input => input.Value));
```

`TryGetNonEnumeratedCount` remains `Scalar` but is excluded; `Count`, `Any`, and
materializers remain eligible. Static and reduced-extension forms use the same
exact declaration entry. `Unknown` must not be interpreted as non-enumerating.
Use a separate consumer policy for provider-facing `Queryable` operations rather
than infer provider execution from this selection.

These facts do not prove execution or enumeration, purity, or later consumption
of a returned deferred sequence. Count getters, callbacks and other user-defined
code can have effects, including indirect enumeration. A potentially consuming
multi-input operation does not imply that every input is consumed. Unsupported
overloads and frameworks receive no guessed consumption facts. The .NET runtime's
[`Count` implementations for .NET 8](https://github.com/dotnet/runtime/blob/v8.0.31/src/libraries/System.Linq/src/System/Linq/Count.cs),
[.NET 9](https://github.com/dotnet/runtime/blob/v9.0.20/src/libraries/System.Linq/src/System/Linq/Count.cs),
and [.NET 10](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Linq/src/System/Linq/Count.cs)
show the enumeration fallback and the distinct non-enumerating count inspection.

## Maintaining the data

The checked-in table records 396 exact overloads from Microsoft.NETCore.App.Ref
8.0.31, 9.0.20 and 10.0.0. To regenerate with those extracted reference packs:

```sh
dotnet run --project tools/DrillPress.LinqCatalogue -c Release -- \
  src/DrillPress.Linq/LinqCatalogueData.cs \
  /path/to/8.0.31/ref/net8.0 \
  /path/to/9.0.20/ref/net9.0 \
  /path/to/10.0.0/ref/net10.0
dotnet csharpier format src/DrillPress.Linq tools/DrillPress.LinqCatalogue
```

The generator requires an explicitly reviewed category for every operation family
and refuses unknown families or conflicting category, role, or consumption facts.
It records direct sequence parameter ordinals from each reviewed framework's
declarations. Review every new overload, its roles and consumption facts in the
generated diff before expanding support. Runtime matching never guesses a
classification from a new method's name or return type.

Microsoft documents the underlying execution distinctions in its
[LINQ execution classification](https://learn.microsoft.com/en-us/dotnet/csharp/linq/get-started/introduction-to-linq-queries#classification-of-standard-query-operators-by-manner-of-execution).
