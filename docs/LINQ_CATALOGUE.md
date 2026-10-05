# Optional standard LINQ catalogue

Add `DrillPress.Linq` at the same exact version as the SDK and import
`DrillPress.Presets`. The base SDK contains no LINQ policy or catalogue dependency.

```csharp
var terminals = Code.Calls.Where(call =>
    StandardLinq.Inspect(call).Category is
        LinqOperationCategory.Scalar or LinqOperationCategory.Materializer);
var sources = terminals.SelectMany(call =>
    StandardLinq.Inspect(call).SequenceInputs.Select(input => input.Value));
```

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
and refuses unknown families or conflicting role maps. It records direct sequence
parameter ordinals from each reviewed framework's declarations. Review every new
overload and its roles in the generated diff before expanding support; runtime
matching never guesses a classification from a new method's name or return type.

Microsoft documents the underlying execution distinctions in its
[LINQ execution classification](https://learn.microsoft.com/en-us/dotnet/csharp/linq/get-started/introduction-to-linq-queries#classification-of-standard-query-operators-by-manner-of-execution).
