# Compatible relationship queries

Implementation discovery returns source definitions in independently compatible
evaluated project views. It includes generated definitions, abstract types and
test projects; consumers choose which of those matter to their policy.

```csharp
var production = Code.Interfaces.ImplementationViews()
    .WhereImplementation(implementation => !implementation.Project.IsTestProject)
    .WhereConcrete();

rules.For(production.Where(view => view.Implementations.Count == 1)
        .At(view => view.Owner))
    .Forbid("ARCH001", "Reconsider this interface.");
```

`WhereImplementation` filters entries inside **every** view. `Where` filters
entire views. Empty views survive entry filtering, supporting zero-count and
missing-implementation rules with the interface as their reportable owner.
`WhereConcrete` means non-abstract class/struct, including records, not public
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

Null checks expose `Form`, `HasNonNullableDeclaration` and `IsKnownNotNullBeforeCheck`
separately. The first is syntax classification, the second is annotation evidence, and
the third is compiler incoming flow evidence. They are never ORed into a runtime
non-null guarantee or used as automatic authority to delete guards.
