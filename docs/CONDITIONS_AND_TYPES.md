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

### Readable validation and type sources

`ConditionPattern.IsEmptyString()` recognizes empty-string equality (including reversed
operands), `is ""`, and `Length == 0`, plus negations. The patterns
`IsNullOrEmptyString()` and `IsNullOrWhiteSpaceString()` recognize their respective
framework calls only. These are different checks: an empty-string comparison does not
reject null, and reading `Length` may throw. They are not rewrite-equivalence proofs.
Use `match.Is(pattern)` or `match.Pattern.Kind` rather than comparing display names.
`CheckedProperty` sees through implicit built-in conversions. `MatchingBranch` preserves
the predicate's polarity, including an `else` arm for a negated empty check; it is not
an alias for the syntactic true branch.

A duplicate `[Required]` policy can collect all four patterns (`IsNull` plus the three
string patterns). With `AllowEmptyStrings = false` all are candidate duplicate checks;
with true, only the null pattern is a candidate. Attribute and branch checks remain
explicit policy, and removing a guard still requires the contextual fix proof.

```csharp
var types = methods.TypesPassedAs("value",
    [controller.Member("Ok"), controller.Member("Created"), jsonResult.Constructor()],
    nested: NestedFunctions.Include)
    .Union(methods.TypesDeclaredBy(genericProduces, produces,
        where: attribute => attribute.ConstructorValue<int>("statusCode").Value == 200));

var models = types.TraverseTypes(new TypeTraversal()
    .UnwrapArrays()
    .ThroughGenericArguments()
    .ThroughProperties()
    .SkippingTypes(pagedResult, 0)
    .WithinLimits(maxDepth: 64, maxTypes: 4096))
    .DeclaredInProject("Api");
```

The grouped argument selector covers methods and constructors under one nested-function
policy (both lambdas and local functions). Attribute identities and type-argument versus
constructor-parameter mappings stay explicit. Without limit overrides, traversal remains
bounded to depth 32 and 1024 visited types per seed. `ThroughProperties()` follows
inherited readable public instance non-indexer properties. `SkippingTypes(wrapper, 0)`
is an opaque-wrapper policy: it emits no wrapper and follows only selected generic
arguments at that node, suppressing its property and custom edges regardless of
configuration order. The selected item types resume ordinary traversal. `StopAt` still
stops every edge, including those selected wrapper arguments.

### Static xUnit discovery

`Code.TestMethods` recognizes xUnit v2/v3 Fact/Theory markers and custom derived
attributes by qualified framework identity. Written base methods appear once; overrides
are separate candidates when marker inheritance is permitted by `AttributeUsage`.
`Code.TestClasses` selects concrete classes in evaluated test projects declaring or
inheriting those methods. `TestDiscovery.Classes(includeAbstract: true)` includes test
base classes. This is static source discovery, not runtime test enumeration, and does
not expand theory data or claim discovery rules for other test frameworks.

Empty-string comparisons also recognize the bound framework `string.Empty` field.
Generic-argument traversal includes arguments on containing types of nested types.
The xUnit preset includes `xunit.v3.core.aot` markers. Reflection-mode v3 discovery
also recognizes default interface test bodies and custom `IFactAttribute` implementations
with a discoverer registration; these reflection-only forms are not inferred from AOT
marker identities. See the [xUnit v3 discovery contract](https://api.xunit.net/v3/4.0.0/Xunit.v3.IFactAttribute.html)
and [AOT differences](https://xunit.net/docs/getting-started/v3/native-aot).
