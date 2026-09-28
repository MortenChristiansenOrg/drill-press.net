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
