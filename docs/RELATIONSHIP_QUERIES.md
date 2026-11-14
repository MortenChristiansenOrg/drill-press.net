# Compatible relationship queries

Implementation discovery returns source definitions in independently compatible
evaluated project views. It includes generated definitions, abstract types and
test projects; consumers choose which of those matter to their policy.

```csharp
var production = Code.Interfaces.ImplementationViews()
    .IgnoringTestProjects()
    .ConcreteOnly();

rules.Rule("ARCH001", "Reconsider this interface.")
    .For(production.WithExactlyOneImplementation())
    .Forbid();
```

`WithExactlyOneImplementation()` requires exactly one implementation in **every**
compatible view of the same physical interface; counts `[1, 1]` qualify, while
`[1, 2]` and `[1, 0]` do not. Each `ImplementationView` is itself reportable at
its `Interface`, so per-view policies can use `Where(view => ...)` directly.

`WhereImplementation` filters entries inside **every** view. `Where` filters
entire views. Empty views survive entry filtering, supporting zero-count and
missing-implementation rules with the interface as their reportable owner.
`ConcreteOnly` means non-abstract class/struct, including records, not public
constructibility or DI activation. No cardinality or production/test preference
is built into discovery.

Partial and constructed generic occurrences count once per implementing source
definition and owning context. `implementation.Contracts` retains actual
constructed interface symbols. `IsDirect` means that exact contract is explicitly
listed among the implementing type's declared interfaces; false means it arrives
through a base class or another interface. Multiple constructions can belong to
one definition. Project graphs and alternative evaluations remain separate even
when target-framework labels are identical. External metadata consumers are not
inventoried. `Implementations.In(...)` keeps its existing counts and caching.

```csharp
var target = CodeType.Named("Product.Base", "Product")
    .Member("Run").WithParameters(CodeType.Of<int>());
var emptyOverrides = Code.Methods.Overriding(target, OverrideSearch.AnyAncestor)
    .WhereBody(MethodBodyShape.EmptyBlock);
```

`Overriding` follows actual compiler override edges. `Immediate` examines only the
direct edge; `AnyAncestor` can match through an intermediate base type.
`OverrideMatches` additionally exposes the matched constructed ancestor and edge
distance. Hiding with `new`, coincidentally named methods, and interface
implementation are not override relationships. Erroneous declaration headers do
not satisfy semantic override predicates; unrelated body errors do not hide a
resolved override edge. Configured assembly/type/signature matching
uses the existing member identity rules.

`BodyShape()` distinguishes `Missing`, `EmptyBlock`, `NonEmptyBlock`, and
`Expression`. Comments/whitespace do not make a block nonempty; `;`, `return;`, and
local-function declarations do. This is a syntax fact, not a claim that invoking
an async or otherwise special method has no effects. Policies and diagnostics
remain in consumer rule bundles.

`Code.Interfaces.ImplementationViews().IgnoringTestProjects().ConcreteOnly()
.WithExactlyOneImplementation()` requires exactly one selected definition in **every**
compatible view of the same physical source interface/project across evaluated contexts.
Counts `[1, 1]` qualify; `[1, 2]`, `[1, 0]`, and no views do not. Entry filtering preserves
zero-count views. Returned declaration memberships retain their compilation contexts.
`view.Interface` names the source interface directly. `DirectlyOverriding(member)` selects
only an immediate edge; `Overriding(member)` keeps the complete ancestor-chain default.

Null checks expose `Form`, `HasNonNullableDeclaration`, `IsKnownNotNullBeforeCheck` and
`CheckedValueOrigin` separately. The first is syntax classification, the second is
annotation evidence, the third is compiler incoming flow evidence, and the fourth says
whether that evidence comes from annotated declarations or from oblivious ones. They are never ORed into a runtime
non-null guarantee or used as automatic authority to delete guards.

## Call paths

`method.Reaches(member)`, `method.PathTo(member)` and `method.Callees()` follow call
paths through `CodeFunction`s: every source declaration with a body a call can run.
That is an ordinary method, a local function, an instance constructor (declared,
primary, or a type's implicit parameterless one), a property, indexer or event
accessor (including an expression-bodied getter), or a user-defined operator or
conversion. `method.Function` is the method's own function; `solution.Relationships
.FunctionOf(symbol)` resolves any source method symbol, and returns null for metadata.

A function runs these members:

| Site | Members |
| --- | --- |
| Invocation, including local functions, query clauses and collection-initializer `Add` | The bound method |
| `new T(...)`, target-typed `new(...)` | The constructor |
| Constructor | Its `this(...)` or `base(...)` initializer (for a primary constructor, the base arguments), the implicit `base()` call, and, unless it chains to `this(...)`, the instance field, property and event initializers of every partial part |
| Property or indexer use | The getter for a read, the setter or `init` accessor for an assignment, both for a compound assignment, increment or `??=`; a ref-returning property's getter for every use |
| `+=` and `-=` on an event | The `add` or `remove` accessor |
| Operator or conversion | The user-defined operator method |

Lambdas and anonymous methods belong to the function that creates them; a local
function is a function of its own, reached where it is called. Pattern-based
calls the compiler makes without a bound operation, such as `foreach` enumerators,
`using` disposal, awaiters and deconstructors, and static constructors, are not
followed.

`CallPaths` chooses which further edges a query follows. `CallPaths.Bound`, the
default, follows only statically bound sites outside delegates:

```csharp
private static readonly CallPaths Callbacks = CallPaths.Bound.ThroughDelegates();
private static readonly CallPaths Services = CallPaths.Bound
    .ThroughDispatch()
    .WithEdge(CodeType.Named("Bus").Member("Publish"),
        CodeType.Named("IConsumer").Member("Consume"))
    .StopAt(function => function.Source.Project.IsTestProject);
```

- `ThroughDelegates()` also follows calls in lambdas and anonymous methods a
  function creates, and method groups it converts to delegates. The SDK can't
  tell whether the delegate runs; each such step has `ViaDelegate` set.
- `ThroughDispatch()` follows virtual, abstract and interface calls to source
  overrides and implementations (`Kind` is `Dispatch`). It considers only
  projects in a compatible view that contains both the declaring project and the
  caller, so separate evaluation contexts and target frameworks stay apart, as in
  `ImplementationViews()`. A call through a closed generic contract such as
  `IHandler<Order>` reaches implementations of that construction and generic ones
  such as `Handler<T>`, not `IHandler<Invoice>` implementations. Members
  declared in metadata, such as `IDisposable.Dispose`, and calls through `base`
  are not dispatched.
- `WithEdge(call, target)` supplies an edge the compiler can't bind: dependency
  injection, reflection, messaging or framework callbacks. Each site that runs
  `call` also runs `target`'s source declarations and their source overrides and
  implementations, in any project that shares a compatible view with the caller
  (`Kind` is `Configured`).
- `StopAt(predicate)` keeps a path from entering selected functions, such as
  approved helpers. The call into a boundary still matches a target, and `Callees`
  lists it, but its own calls are not followed unless the query starts there.

Queries cache their answers per analysis, target instance and `CallPaths`
instance, so keep both in static fields. Each function's sites are found once,
the first time a query reaches it; the type indexes behind dispatch and
configured edges are built only when a query uses those options.

`method.PathTo(member, paths)` returns the shortest `CallPath`, or null. Its
`Steps` say which function made each step, the member it ran, the `Kind` of edge
and whether it is `ViaDelegate`; `IsBound` is true when every step is a bound
call outside delegates. A path is reportable at its first step.
`function.Steps(paths)` lists one function's own steps, including calls to
metadata members.

`method.Callees(sameType, transitive, paths)` lists the source functions a method
calls, in the order the sites appear and then breadth first, each once.
`sameType` keeps functions of the method's own type, including other partial
parts and its local functions; `transitive` follows the callees' calls too. The
method itself appears only when it is called. Callees resolve to the function in
the declaring project's context, and generated functions are included: check
`callee.Source.Document.IsGenerated` if they don't belong in a finding.

A negative answer only says that the modeled paths don't reach the target. It
does not prove that runtime execution can't, for example through reflection,
dependency injection the rule did not configure, or a delegate stored and run
elsewhere.
