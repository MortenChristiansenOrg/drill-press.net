# Bound expressions and executable scopes

The following APIs extend the existing query model. Results belong to an evaluated
compilation context; an equal source span in another framework is not the same
semantic fact. Queries remain lazy and cached per analysis. Framework and naming
policies remain in consumer rules.

```csharp
var calls = OperationQueries.Invocations
    .Calling(configuredMembers)
    .WhereReceiver(receiver => receiver.TypeIsOrDerivesFrom(clientType))
    .WhereArgument("mode", argument => argument.IsExplicit
        && argument.Value?.IsConstant(modeType, expectedValue) == true);

var urls = calls.ArgumentsFor("url").SourceValues();
rules.For(urls).Require(value => allowedRoute(value), "URL001", "Use an approved route.");
```

`CodeMember.WithSignature(new MethodSignature(...))` refines generic arity,
static shape, constructed type arguments, return type/ref kind and parameter ref
kinds. `WithParameters` continues to select parameter types. `Calling` retains
the actual selected method and rejects erroneous binding; it does not equate
overloads. Assembly qualification retains its existing meaning.

`CodeInvocation.Parameter(name)` returns a group even for an empty expanded
`params` parameter. `ArgumentsFor(name)` enumerates its values. `SourceIndex`
records source order independently of the parameter ordinal. Default arguments
have no editable expression or location; `SourceValues()` deliberately excludes
them. `WhereArgument` uses **any** matching value; use the parameter group for
all/none tests. Array and collection `params` expansion retains each input.

`ReceiverKind`, `Receiver`, `IsConditional`, and `EvaluationInputs()` distinguish
instance receivers, normalized extension receivers and ordinary static calls.
An implicit `this` receiver exposes its `Type` but has `IsImplicit == true`, no
independent source expression, and default `TypeInfo`. Its `Syntax`/`Location`
anchor the call. Evaluation inputs retain source order and do not assert purity
or unconditional execution of conditional-access arguments.

`CodeExpression` exposes original and converted type information, bound symbol,
constant availability, conversion, declaration nullability, and raw operation.
Typed constant matching distinguishes enum types and constant null from absence.
`TypeIsAssignableTo` accepts identity/implicit reference conversions, excluding
boxing and user-defined conversions. Named/open descriptors match actual nominal
ancestors; `Of<T>()` additionally resolves non-nested constructed targets for
variance. The contextual symbol overload covers arbitrary exact target types.

```csharp
var tests = Code.Methods.WithAttribute(configuredMarkerTypes);
var owners = tests.ContainingTypes();
var branches = tests.Body(NestedFunctions.Exclude)
    .ControlFlowNodes(ControlFlowKinds.If | ControlFlowKinds.Loop
        | ControlFlowKinds.ConditionalExpression);
```

`WithAttribute` recognizes derived attribute classes against the configured
marker assembly; it does not restrict a consumer-defined derived attribute to
that assembly. It inspects declared attributes, not runtime inheritance from
base methods. `symbol.Attributes()` exposes `CodeAttribute` evidence;
`Matches(marker, includeDerived: false)` selects exact attribute classes.
`NamedArgument` distinguishes missing arguments from explicitly supplied
false/null/invalid constants. `ConstructorArgument` maps parameter names. The
SDK never runs attribute constructors or infers arbitrary property defaults.
`ContainingTypes` deduplicates partial declaring types within each context.

Body scopes include block and arrow roots, but omit no-body methods. Exclusion
prunes local functions, lambdas and anonymous methods. `NestedBodies()` selects
them independently; inclusion traverses their bodies without treating signature
defaults as outer executable code. Overlapping scope projections deduplicate
occurrences. `CodeNode.ContainingSymbol` exposes the actual executable owner.
Control-flow categories are explicit flags, not an evolving test-style policy.
Syntax selections include unreachable code; presence does not prove execution.

```csharp
var checks = tests.Body().NullChecks();
var nonNullReferences = checks.Where(check =>
    check.Domain == NullCheckDomain.Reference
    && check.FlowStateBeforeCheck == NullableFlowState.NotNull);
```

Null checks recognize null patterns, built-in equality in either order, and
framework nullable `HasValue` with Boolean negation/constant comparison. Custom
operators and same-named properties do not qualify. Parentheses/negation yield
one normalized occurrence, and nullable value presence remains a separate domain.

Roslyn can reset an operand's flow state during a pure null test. Raw
`CheckedValue.TypeInfo` remains unchanged compiler evidence. The lazy
`FlowStateBeforeCheck` probes the incoming state at a supported statement entry:
a direct if/return condition, single local initializer, or simple assignment.
It returns `None` for compound evaluation, loop conditions, arrow bodies and
other unsupported positions rather than substituting an earlier state. Disabled
nullable analysis likewise supplies no incoming state. See Roslyn's
[nullable flow rules](https://github.com/dotnet/roslyn/blob/main/docs/features/nullable-reference-types.md).

Neither annotations nor compiler flow establish runtime non-nullness in a program
violating its contracts. None of these queries authorizes deleting an expression,
getter evaluation or branch. Generated code follows the selected query root;
ordinary method/source roots exclude generated declarations from findings.

### Composing scoped invocation rules

Method and source-element queries accept `InProject("Api")`, `InFolder("Endpoints")`,
`InFilesNamed("*Tests.cs")` and `InNamespace("Contoso.Api.**")`. Folder matching uses
whole physical path segments relative to each project, at any depth; it does not use
MSBuild `Link` folders. Loose-source rules can pass `sourceRoot` to `InFolder`, which
otherwise uses the invocation directory. Namespace `**` includes its root namespace.
`Named("HandleAsync").DeclaredInTypesDerivedFrom(baseType)` excludes the base itself;
`ContainingType?.IsOrDerivesFrom(baseType)` includes it. Open generic bases match
constructed ancestors. `Code.Types.ImplementingInterface(contract)` includes inherited
interfaces.

```csharp
var urls = OperationQueries.Invocations
    .InProject("Api.Tests")
    .OnReceiverOfType<HttpClient>()
    .ToMethodsNamed("GetAsync", "PostAsync")
    .ArgumentsOfTypes([CodeType.Of<string>(), CodeType.Of<Uri>()], "url", "requestUri")
    .SourceValues();
```

Receivers include derived classes, both extension spellings, conditional calls and
calls inside lambdas. Argument selectors use bound parameters, not source positions.
`references.PassedAs("comparer").To(distinct)` follows a directly passed member through
parentheses and implicit built-in conversions; a member nested inside another
computation does not match. Each selected argument exposes its `Invocation`.

`member.WithParameters(...).OptionallyFollowedBy<StringComparison>()` describes two
exact overload signatures. Combine families with `Union`. Use
`WhereArgumentOrMissing("comparisonType", argument => argument.Is(StringComparison.Ordinal))`
when the parameterless family member should match. Omitted optional arguments retain
their effective compiler constant; `IsOmittedOr(value)` explicitly accepts omission
regardless of that default. None of these selections proves replacement equivalence.

`CodeBody.Calls(member, withArgument: "key", equalTo: property.Name)` uses typed
compiler constants (including `nameof`) and respects the body's nested-function policy.
Missing bodies contribute no scope. Attributes expose `ConstructorValue<T>(name)` and
`NamedValue<T>(name)` as optional typed constants, preserving missing values separately
from false/zero/null. Enum reads require enum identity; arrays are not scalar constants.
`FlagOrDefault(name, fallback)` requires an explicit fallback and never executes
attribute code or reads runtime property initializers.

`Union` retains one candidate per physical occurrence and evaluated source membership;
`Concat` preserves duplicates. An explicit equality comparer can override union identity.
`expressions.WithGroupsFrom(groups)` retains every expression with all matching groups.
`UniqueGroup` is null for zero or multiple matches, allowing the finding to remain while
a fix is withheld. Group selection never chooses the first match implicitly.

### Inspecting string construction

`expression.AsBuiltString()` exposes ordered `Parts` and decoded `LiteralParts` for
literals (including raw/verbatim strings), interpolations and built-in concatenation.
Constant references remain holes even if the entire expression has a compiler constant;
`expandConstants: true` explicitly expands constant string references. Each hole keeps
its source expression, alignment and format. User-defined concatenation stays opaque.
`StartsWith(hole => ...)` tests the first nonempty source segment without evaluating it.

Constructor projection is explicit: `uri.ConstructorArgument("relativeUri")` inspects
the bound source argument before calling `AsBuiltString()`. For a single-string URI
constructor use its `uriString` parameter. No URL-specific policy, overload guessing,
culture formatting or runtime value inference is built into this model. A rule can
therefore distinguish a literal `/details` suffix from `?next=/details`, and inspect
relative URI text without accidentally treating its base URI as the selected value.

## Call and expression views

`call.Expression` and `calls.Expressions()` expose the existing `CodeExpression`
model with the bound invocation's source span, context, original/converted types
and `Facts`. Implicit or invalid calls have no expression view. Conditional
access exposes only the bound call arm, such as `.Load()`, rather than the whole
`receiver?.Load()` expression. `expression.AsInvocation()` returns a bound call
through parentheses and implicit built-in conversions; explicit/user conversions,
conditional-access envelopes, and non-call/invalid expressions return null.
`calls.OutsideExpressionTrees()` is an explicit consumer filter. Ordinary call
queries retain expression-tree occurrences. Built-in projections retain lazy
query caching, occurrence identity and coverage collection dependencies.

## Contextual project scopes

`Code.Calls.InNonTestProjects().InProjectsWithType(contract)` shares evaluated
project classification and contextual type availability with other source queries.
`project.HasType(contract)` requires one match; `project.InspectType(contract)`
retains missing, available and ambiguous outcomes and caches them per context.
Assembly-qualified descriptors can distinguish same-named types. Availability
includes references exposed through extern aliases; it does not imply an
unqualified source name can bind, source usage, or a direct package reference.
Open generic names select definitions; `CodeType.Of<T>()` also resolves ordinary
constructed types and arrays. Nested constructed runtime types remain unavailable
when their outer substitutions cannot be resolved.

## Bound call families

`ToMethodsDeclaredOn(type)` matches the call's normalized declaration owner;
`ToMethodsDeclaredOnOrDerivedFrom(type)` additionally follows the owner's nominal
base classes. These differ from receiver filtering: an inherited member stays
owned by its base type, an override is owned by the overriding type, and an
extension stays owned by its static class in either call spelling. Metadata and
constructed generic owners retain their identity. `ToMethodsMatchingName("*Async")`
uses the existing case-sensitive glob semantics; `ToMethodsNamed` still means
exact names. Equivalent element predicates are `IsDeclaredOn`,
`IsDeclaredOnOrDerivedFrom`, and `TargetNameMatches`. Compose with `Calling` to
retain configured signature constraints. Invalid calls never match.

## Configured source-input traversal

```csharp
var adapters = new ExpressionTraversal()
    .ThroughReceiverOf(asEnumerable, asAsyncEnumerable)
    .ThroughArgumentOf(customAdapter, "source");
var result = expression.TraverseInputs(adapters);
var hasQuerySource = result.Values.Any(value => value.TypeIsAssignableTo(queryableType));
```

Configuration is immutable and contains consumer-selected call identities and
input roles. Extension receivers work in both spellings; named arguments retain
their original values and source order. Parentheses and implicit built-in
conversions do not obscure a call. Results include the root and follow configured
inputs in depth-first source order, once per source occurrence and context.

`Status` and typed `Boundaries` distinguish complete traversal, unavailable
binding/inputs, and limits. Defaults are 16 steps and 256 expressions; set
`maxDepth` and `maxExpressions` explicitly when needed. Omitted inputs, implicit
`this`, dynamic calls, conditional-access envelopes, and explicit/user conversions
are unavailable. Unconfigured calls, local/parameter references, and properties
are intentional boundaries: traversal does not chase assignments, method bodies,
property implementations, or runtime aliases. An adapter match proves no purity,
runtime object identity, or permission to remove or rewrite that adapter.
