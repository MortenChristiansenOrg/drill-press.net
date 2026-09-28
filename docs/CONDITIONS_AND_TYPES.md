# Bound conditions and static type reachability

These APIs assemble reusable evidence. Validation-framework behavior, serializer
conventions, HTTP status selection and application ownership remain rule policy.

## Conditional validation evidence

`methods.Body().Conditions().Checks(patterns)` selects complete `if` and ternary
conditions. Use `ConditionPattern.NullTests()` for built-in null/nullable presence
checks and `ForCall(member, valueParameter, classification)` for configured Boolean
methods such as `string.IsNullOrEmpty`. Exact identity comes from `CodeMember` and
its assembly/signature refinements. Ordinary negation and Boolean comparisons
normalize polarity. Compound conditions and overloaded Boolean operators do not
produce a guessed match.

A match exposes `CheckedValue`, `Pattern.Name`, the complete condition location,
`MatchingOutcome` and `MatchingBranch`. Inspect `CheckedValue.Symbol` as a property,
and its `Attributes()` for typed constructor/named values. Absent named arguments
are distinct from explicit false/null; consumer policy supplies known defaults.
Branch `Invocations()` excludes nested functions by default. A call's syntactic
presence is not a proof that it runs on every path. No deletion is implied.

```csharp
var matches = methods.Body().Conditions().Checks(
    ConditionPattern.NullTests(),
    ConditionPattern.ForCall(
        CodeType.Of<string>().Member("IsNullOrWhiteSpace"), "value", "whitespace"));
```

`MemberKeyResolver.Resolve(key, selectedModelRoot)` binds literal names or `nameof`
chains to explicit receiver evidence. The default grammar accepts a single member
name; opt into `MemberKeyGrammar.DottedAndIndexed` for paths such as
`Items[0].Address.Name`. It supports C# identifiers and constant int/string array or
indexer arguments. Custom parsing, serialized-name mapping and visibility are
ordinary delegates. Missing/ambiguous members, inaccessible defaults, unsupported
indices and wrong-root `nameof` return null.

`resolved.CompareTo(check.CheckedValue)` returns `Match`, `Different` or `Unknown`.
It compares actual substituted symbols and typed indices. It never treats matching
text as identity. Root comparison identifies the same local/parameter storage or
`this` in one compilation; it is not a time-invariant runtime-alias proof. An
explicit root-relation delegate can supply additional evidence. Variable indices,
dynamic access and nonidentity casts remain unknown. Merely equal property names,
overrides or two objects of the same type are not interchangeable.

## Static type graph

`methods.TypeSeeds(selectors)` uses `TypeSeedSelector` factories for constructed
attribute type arguments, `typeof` constructor/named values, bound invocation
arguments and object-creation arguments. Attribute/call predicates can select
success statuses or configured overloads. Additional selectors are normal
`Func<CodeMethod, IEnumerable<TypeSeed>>` functions. Each seed retains its anchor,
context, constructed static type and origin evidence.

```csharp
var reached = handlers.TypeSeeds(
    TypeSeedSelector.AttributeTypeArgument(responseAttribute, 0, IsSuccess),
    TypeSeedSelector.InvocationArgument(okMethod, "value"),
    TypeSeedSelector.ConstructorArgument(resultConstructor, "value"))
    .TraverseTypes(new TypeTraversal(maxDepth: 32, maxStates: 1024)
        .UnwrapArrays()
        .Unwrap(pageType, [0])
        .ThroughProperties())
    .WhereType(IsApplicationModel);
var declarations = reached.Declarations(project => IsOwned(project));
```

`Ok(new Widget())` seeds `Widget` before conversion to `object`; an `object`
variable seeds only `object`. There is no runtime/interprocedural type recovery.
Wrapper traversal is explicit; unknown generics are not automatically transparent.
Wrappers can be emitted or omitted while their children are still traversed.
Property edges retain generic substitutions and default to public getters on
inherited, instance, non-indexer properties. Options and predicates refine those
source-language facts; they do not establish serializer behavior.

`WhereType` filters output only. `StopAt` explicitly prunes outgoing edges. Visited
sets use constructed symbols in the seed context, preserving `Box<A>` and `Box<B>`
until their distinct property edges have been explored. Definition/partial
deduplication occurs only in `Declarations`, using actual source declaration
references and the evaluated dependency graph. Alternate framework contexts remain
separate. Generated-only and metadata types can be intermediate nodes but never
become ordinary-source findings.

Each `TypeReachability` retains `Status`, `IsComplete`, `VisitedCount` and its seed,
even if no declaration is found. Ordinary cycles terminate; expanding generic
recursion yields `DepthLimit`/`StateLimit`, and selected unresolved types/edges yield
`Unresolved`. Positive results remain useful; absence/inventory rules must require
complete evidence. Complete means complete for the configured static graph, never
for all possible runtime payloads. A deliberate boundary or resolved `object` leaf
is not itself an unresolved compiler edge. Queries are lazy and cached per solution.
