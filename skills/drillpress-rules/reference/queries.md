# Queries and candidates

Everything is in `namespace DrillPress`. `CodeQuery<T>` is a lazy, reusable selection
cached per analysis; materialize one with `query.In(solution)` in tests.

## Roots (`Code.*`)

| Root | Yields | Notes |
| --- | --- | --- |
| `Files`, `FilesIncludingGenerated` | `CodeFile` | `Path`, `Name`, `Folder`, `RelativePath` (null outside the project directory), `HasTopLevelStatements`; reports at 1:1 |
| `Projects` | `AnalysisProject` | one per evaluated context (per target framework) |
| `Types`, `Interfaces` | `CodeTypeDefinition` | partial types once per context |
| `TypeDeclarations` | `CodeTypeDeclaration` | every written part; `TopLevel()` |
| `Methods` | `CodeMethod` | ordinary methods, not constructors, accessors or local functions |
| `Fields` / `Properties` / `Parameters` | `CodeField` / `CodeProperty` / `CodeParameter` | one field candidate per variable; parameters include lambdas and primary constructors |
| `EnumMembers` | `CodeEnumMember` | enum members (not in `Fields`): `Value`, `HasExplicitValue`, `ContainingType` |
| `Declarations` | `CodeSymbol` | every declared symbol, including events, locals, accessors |
| `TestMethods`, `TestClasses`, `AbstractTestClasses` | `CodeMethod`, `CodeTypeDefinition` | xUnit, NUnit, MSTest markers incl. derived attributes |
| `Calls` | `CodeInvocation` | every bound call in bodies, initializers, attributes, top-level code |
| `ObjectCreations` | `CodeObjectCreation` | `new T(...)`, target-typed `new(...)` |
| `Routes`, `RouteGroups` | `CodeRoute` | minimal API `MapGet`/`MapPost`/... routes and `MapGroup` groups |
| `MemberReferences` | `MemberReference` | named references to fields, properties, methods (method groups, `nameof`) |
| `TypeReferences` | `CodeTypeReference` | every written use of a type; aliases resolved; `var` excluded |
| `ReferencesTo(symbol)` | `CodeSymbol` | references across compilations by identity |
| `IfStatements` | `CodeIfStatement` | `Then`, `Else`, `Branches`; `WithElse()`, `Branches().WithoutBraces()` |
| `EmbeddedStatements` | `CodeBranch` | if/else branches and for, foreach, while, do, using, lock and fixed bodies; `HasBraces`, `IsElseIf`, `IsStacked`; `WithoutBraces()` |
| `Catches` | `CodeCatch` | report at the clause header |
| `LocalVariables`, `ForEachLoops`, `OutVariables` | `CodeVariableDeclaration` | `int a = 1, b = 2;` is one candidate |
| `Enumerations` | `CodeEnumeration` | `foreach` loops incl. deconstruction and `await foreach` |
| `NullChecks` | `CodeNullCheck` | `== null`, `is null`, `is not null`, `??`, ... |
| `Comments` | `CodeComment` | `Text`, `Kind`, `ContainingDeclaration` |
| `Nodes<TSyntax>()` | `CodeNode<TSyntax>` | any `Microsoft.CodeAnalysis.CSharp.Syntax` type |
| `Operations<TOperation>()` | `CodeOperation<TOperation>` | any `Microsoft.CodeAnalysis.Operations` type |

File selections narrow discovery before binding: `files.Calls()`, `files.Nodes<T>()`,
`files.Operations<T>()`, `files.Declarations()`, `files.Comments()`.

## Query operators

- `Where(predicate | RuleCondition<T>)`, `ExceptWhen(...)`.
- `Select`, `SelectMany`, `Concat`, `Union(other, comparer?)`.
- `Join(other, key, otherKey, (a, b) => result, comparer?)` for matching pairs.
- `WithoutMatching(other, key, otherKey)` or `WithoutMatching(other, (a, b) => match)`
  for owners missing a counterpart.
- `CodeQuery<T>.Create(solution => ...)` for whole-selection logic such as grouping.
- `AnalysisFact<T>(solution => ...)` / `ProjectFact<T>(project => ...)` cache shared
  values; `fact.SelectMany(value => ...)` turns one into a query.

## Discovery and reuse

Name constraints (`To`, `ToMethodsNamed`, `ToMethodsMatching`, `member.References`,
`type.References`) and place filters carry through `Where` and `ExceptWhen` in either
order. Call, operation, member-reference, type-reference and object-creation roots
skip other documents before binding. `InNamespace` needs binding and does not narrow
discovery. Apply these filters before reshaping with `Select`, `SelectMany`, `Union`
or `Concat`; a custom `Where` alone cannot narrow discovery.

`ObjectCreations.Of(type)` also narrows by written type name, keeping aliases,
keyword types and target-typed creations. Exact type-reference names use an index;
aliases still resolve semantically. Syntax and file-scoped `Nodes<T>()` roots apply
place filters before enumeration. Cached inventories stay complete for later queries.
Operations bind once per document, in parallel while preserving candidate order;
compiler-generated calls are included. Name/type checks run before `IsResolved`;
its error check compiles each enclosing member once, falling back to the exact span
when that member has errors. Reuse query instances to share materialization.
Path, namespace, project and name glob patterns are compiled once per distinct pattern.

## Shared filters

Every query of `ICodeElement` (anything reportable):

- `InProject(glob)`, `InTestProjects()`, `InNonTestProjects()`, `InProjectsWithType(type)`
  (also on `Code.Projects`)
- `InNamespace("A.B")`, `"A.*"` (one segment), `"A.**"` (A and descendants)
- `InFolder("Domain")` (whole segments at any depth), `InFilesNamed("*Tests.cs")`
- predicates: `element.IsInProject(glob)`, `IsInNamespace(pattern)`, `IsInFolder(folder)`
- enclosing code: `element.IsInType(type)` (and its containing types), `ContainingMethod()`
  (walks out of lambdas and local functions), `EnclosingLoop()`, `IsInLoop()`,
  `IsGuardedByCatchAll()`, `UnguardedHandler()` (`HandlerClause.None`, `Catch` or `Finally`).
  Loops and try statements count only in the element's own function; catch filters are
  not evaluated. `ContainingMethod()` returns the enclosing method, constructor, accessor
  or operator, or null outside one; raw `ContainingSymbol` keeps lambdas/local functions
  distinct. `EnclosingLoop()` returns `CodeNode<StatementSyntax>?` and includes loop
  headers (such as a foreach collection), without following calls.

Every query of `ICodeDeclaration` (types, type parts, methods, fields, properties,
parameters, enum members, symbols):

- `Named("A", "B")`, `NameMatching("*Async")`, `WithNameContainingAnyWord(["Temp"])`
- `WithAttribute(type, ...)` (derived attribute classes match), `WithAccessibility(Accessibility.Public)`
  (effective, including defaults), `WithExplicitModifier(Modifier.Static)` (written tokens)
- members: `Name`, `NameWords`, `Symbol` (nullable), `Accessibility`, `HasExplicitModifier(m)`
- extensions: `NameStartsWith`, `NameEndsWith`, `NameMatches`, `NameContainsWord(word)`,
  `Attributes()`, `HasAttribute(type)`, `HasAttribute(type, attribute => ...)`,
  `HasDocumentationComment()`, `ExplicitModifier(m)` (token for `ReportAt`)

`Modifier`: `Public`, `Private`, `Protected`, `Internal`, `File`, `Static`, `Abstract`,
`Sealed`, `Partial`, `ReadOnly`, `Async`, `Const`, `Virtual`, `Override`, `New`, `Extern`,
`Required`, `Volatile`, `Unsafe`, `Ref`, `Out`, `In`, `Params`, `This`, `Scoped`.

## Declarations

- `CodeTypeDefinition`: `Namespace`, `IsClass`, `IsInterface`, `IsStruct`, `IsEnum`,
  `IsRecord`, `IsAbstract`, `IsStatic`, `IsNested`, `HasOnlyStaticMembers`,
  `IsTopLevelProgram` (the generated `Program`, including its partial declaration),
  `Implements(type)`, `DerivesFrom(type)`, `IsOrDerivesFrom(type)`, `Symbol` (non-null), `Syntax`, `Solution`.
  `HasOnlyStaticMembers` requires at least one written member, all static, constants or
  nested types; compiler-generated members do not count.
  Queries: `DerivedFrom(types)`, `ImplementingInterface(type)`, `ImplementationViews()`.
- `CodeTypeDeclaration`: `IsTopLevel`, `Syntax`, `Symbol`; attributes are those written on this part.
- `CodeMethod`: `IsAsync`, `IsStatic`, `ReturnsVoid`, `ReturnTypeIs(type)`, `Parameters`,
  `HasBody`, `HasEmptyBody`, `HasNoStatements`, `Body(nested?)`, `ContainingType`,
  `Flow`, `Reaches(member)` (static call paths through loaded source), `Syntax`,
  `Callees(sameType: false, transitive: false, nested: Include)` (source methods it calls,
  in discovery order, deduplicated; transitive traversal is breadth-first).
  `sameType` includes other partial parts; `nested` defaults to `NestedFunctions.Include`.
  Only ordinary statically bound source methods count, not constructors, accessors,
  method groups, delegate targets, runtime virtual dispatch or metadata. Recursion can
  include the original method.
  Queries: `DeclaredInTypesDerivedFrom(types)`, `Overriding(member)`,
  `DirectlyOverriding(member)`, `WhereBody(MethodBodyShape)`, `ContainingTypes()`.
- `CodeEnumMember`: shared declaration members, `Syntax`, `ContainingType`, `Value`
  (written or implied constant; null when binding fails), `HasExplicitValue`; reports
  at its name.
- `CodeField`: `Type`, `TypeIs<T>()`, `IsConst`, `IsReadOnly`, `IsStatic`, `Initializer`, `Declaration`.
- `CodeProperty`: `Type`, `TypeIs<T>()`, `IsStatic`, `HasGetter`, `HasSetter`, `HasInit`,
  `IsAutoProperty` (compiler-supplied storage only), `Initializer`.
- `CodeParameter`: `Type`, `TypeIs<T>()`, `Ordinal`, `ContainingSymbol`, `IsLambdaParameter`,
  `HasDefaultValue`, `DefaultValue`.

## Statements and syntax

- `CodeIfStatement`: `Then`, `Else`, `Branches`, `InconsistentlyBracedBranch` (unbraced side
  when only one side of an if/else has braces). `CodeBranch`: `HasBraces`, `IsElseIf`, `IsStacked`
  (nested `using` or `fixed` of the same kind). `WithoutBraces()` skips all three.
- `CodeCatch` (`new CodeCatch(source, clause)` for a try statement's `Catches`): `ExceptionType`
  (null for `catch { }`), `Catches<T>()`, `CatchesAnyException`,
  `HasFilter`, `Filter`, `Body`, `IsEmpty`, `Rethrows`.
- `CodeVariableDeclaration`: `TypeName` (reportable), `HasExplicitType`, `HasInitializer`,
  `CanUseVar`, `Variables`; queries `WithExplicitType()`, `WithInitializer()`, `WhereVarPreservesType()`.
- `CodeNode<T>`: `Syntax`, `ContainingSymbol`, `Operation`, `TypeInfo`, `Constant`
  (`HasValue` first), `AsExpression()`; query `Duplicates(minimumTokens)` (repeated token shapes).
- `CodeBody` (`method.Body()`, `methods.Body()`, `CodeBody.Of(source, node)` for any syntax
  node, `nested: NestedFunctions.Exclude` by default): `Source`, `Root`, `Nested`,
  `Calls()`, `Calls(member)`, `Nodes<T>()`, `ControlFlowNodes(kinds)`, `NestedBodies()`,
  `IsEmpty`, `BlankLines`, `Sections()` (`CodeSection`: statements between blank lines, as in Arrange/Act/
  Assert, with `Statements`, `Index`, `Calls()`, `Nodes<T>()`); `bodies.Conditions().Checks(pattern)`
  with `ConditionPattern.Null`, `EmptyString`, `NullOrEmptyString`, `NullOrWhiteSpaceString`,
  `ForCall(...)`; `bodies.NullChecks()`. Sections have a zero-based index and report from
  their first statement through their last; comment lines and blank lines inside a
  statement, comment or disabled code do not split sections. Expression bodies have
  no sections or blank lines. `BlankLines` excludes multiline tokens and, by default,
  nested functions.

## Types and members

- `CodeType.Of<T>()`, `CodeType.Named("Ns.Outer+Inner")`, `Named("Ns.Map<,>")` (open
  generic), `Named(name, assemblyName)`, `CodeType.Framework("System.Linq.Enumerable")`
  (framework assembly only), `CodeType.FromSymbol(namedType)` (exact constructed arguments;
  an original definition gives an open descriptor). `type.References` selects its
  `CodeTypeReference`s.
- `type.Member("Name")` (all overloads, fields, properties), `.WithParameters(types)`
  (one overload; none = parameterless), `type.Constructor(types)`.
- `member.References` (`MemberReference`: `Symbol`, `Expression`, `Facts`, `AsArgument()`);
  `references.OutsideNameOf()`; `Code.MemberReferences.To(memberA, memberB)` for several.
- `ApiSet(members)`: `Contains(method)`, `Union(other)`; usable with `calls.To(set)`.
- `CodeTypeReference`: `Type`, `RefersTo<T>()`, `RefersTo(type)`.
- Raw symbols: `symbol.Attributes()` → `CodeAttribute` (`ConstructorValue<T>(name)`,
  `NamedValue<T>(name)`, `Matches(type)`); `Symbols.HasAttribute`, `IsOrDerivesFrom`,
  `DerivesFrom`, `Implements`; `symbol.NameMatches`, `IsInNamespace`.
- Signatures: `method.ImplementsOrOverrides()` (another declaration decides its signature),
  `IsEventHandler()` (object sender, EventArgs or a derived type); `type.IsTask()`
  (framework Task/ValueTask, with or without a result; custom awaitables are not inferred), `UnwrapTask()` (the
  result type of a generic task, otherwise the type unchanged).
- `method.AddingParameter(type, matchNames: true)`: same-name overloads adding one parameter
  of the type and keeping the others (names, ref kinds and arity too unless `matchNames:
  false`). Types compare by identity, ignoring nullable annotations. Candidate discovery,
  not proof that the overload behaves the same. No candidate is returned if the
  original already takes that type. Generic overloads compare with the call's type
  arguments.

## Calls, arguments and expressions

- `CodeInvocation`: `Target` (chosen overload), `Receiver`, `Arguments`,
  `Argument("name")` (bound, including omitted defaults; null for unknown names),
  `ArgumentsFor("name")`, `Calls(member)`, `IsDeclaredOn(type)`, `TargetNameMatches(glob)`,
  `IsConditional` (`?.`), `Expression`, `IsResolved`, `Chain()`. Reports at the method
  name; `ReportAt(call => call.Operation.Syntax)` reports the complete call.
  Equality/hash identity is the source membership and bound operation, so separately
  wrapped views of the same call match inside a chain.
- `CodeCallChain` (`call.Chain()`): `Calls` in evaluation order (`Where`, `OrderBy`, `ToList`
  in `items.Where(a).OrderBy(b).ToList()`), `First`, `Last`, `Root` (the value it starts
  from), `Local`, `Before(call)`, `After(call)`. `ThroughLocal()` adds the chains assigned to
  the root local and the chains that start from it in the same body (`q = q.Where(...)`,
  builders changed in place), in source order, ignoring statement order and branches.
  It includes nested functions, stops at a second local, and returns only this chain
  when its root is not a local. Instance, extension and conditional-access calls link;
  an await, explicit cast or other intervening expression ends a chain.
- Call filters: `To(member | apiSet)`, `ToMethodsNamed(names)`, `ToMethodsMatching(glob)`,
  `ToMethodsDeclaredOn(type)`, `ToMethodsDeclaredOnOrDerivedFrom(type)`,
  `OnReceiverOfType<T>()`, `WhereReceiver(expr => ...)`, `WhereArgument(name, arg => ...)`,
  `WhereArgumentOrMissing(name, ...)`, `WhereAnyArgument(...)`, `OutsideExpressionTrees()`,
  `ArgumentsFor(name)` → `CodeQuery<CodeArgument>`, `.SourceValues()` → written values.
- `CodeArgument`: `Parameter`, `Name`, `Value`, `IsExplicit`, `IsReceiver`, `Is(value)`,
  `IsOmittedOr(value)`, `ValueAs<T>()`, `TextValue`, `Invocation`.
- `CodeObjectCreation`: `Type`, `Constructor`, `Creates<T>()`, `Arguments`, `Argument(name)`,
  `IsTargetTyped`; query `Of<T>()`, `Of(type)`.
- `CodeExpression`: `Type`, `TypeIs<T>()`, `ConvertedTypeIs`, `TypeIsOrDerivesFrom`,
  `TypeIsAssignableTo`, `Constant`, `Is(value)`, `IsConstant(type, value)`, `TextValue`,
  `Symbol`, `RefersTo(member)`, `IsNameOf(symbol)`, `FlowState`, `MayBeNull`,
  `DeclaredNullability`, `Facts` (`IsInsideNameOf`, `IsInsideExpressionTree`, comments),
  `AsInvocation()`, `AwaitedCall()` (the call an await awaits, through conversions and
  `ConfigureAwait`). String construction and collection selection are below.
- Culture and clock: `call.ComparesWithCulture()`, `call.FormatsWithCulture()`,
  `hole.FormatsWithCulture()` (a `CodeNode<InterpolationSyntax>`),
  `call.WaitsWithoutTimeProvider()`, `creation.TimesWithoutTimeProvider()`. They classify
  common framework APIs, never which culture or clock to use. Culture classification
  concerns calls missing a comparison/comparer/provider; interpolation classifies
  fractions, non-invariant date/time formats and integer digit grouping, not integer
  signs alone; integer parsing counts with non-constant number styles or styles allowing
  thousands, a decimal point or currency. Clock queries cover Task.Delay, timed
  WaitAsync, Thread.Sleep, CancelAfter and timer/delayed CancellationTokenSource creations; a local source
  initialized with a TimeProvider is excluded from CancelAfter results.
- `CodeRoute`: `Mapping` (reports here), `Path` (with constant group prefixes; null when
  not constant), `Configuration`, `GroupConfiguration`, `HandlerExpression`, `Handler`,
  `IsConfiguredWith(member, includeGroups: false)`. Groups have no handler. Group
  prefixes follow local initializers (not assignments), at most ten local hops per
  group; a local or call holding the application, such as `builder.Build()`, has no
  prefix. Apply project filters explicitly; not an authorization analysis.
- `element.ValuesInScope<T>()` / `ValuesInScope(type)`: `ScopedValue` (`Symbol`, `Name`,
  `Kind` (`ScopedValueKind`): `Local`/`Parameter`/`Field`/`Property`, `Type` with nullable annotation) for each
  value of the type (or a derived/implementing type) readable by name at the element,
  nearest first; instance state is left out in static contexts, the locals and
  parameters around a static lambda or local function are left out inside it, and an
  outer type's instance state is left out in a nested type. Use it to find the
  `CancellationToken` or `TimeProvider` to pass on. Locals come latest-first, then
  enclosing/primary-constructor parameters, then fields/properties by name. A local
  in its own initializer, later locals and values reached through another value
  (`options.Clock`) are excluded.
- `element.ExplanatoryComment()` / `HasExplanatoryComment()`, `source.ExplanatoryComment(position)`:
  the ordinary comment on the line, on the line above, or above the statement or member;
  documentation and `drillpress-allow` comments do not count. Use for "explain this `!`".
- `CodeNullCheck`: `CheckedValue`, `Polarity`, `Domain`, `Form`, `IsKnownNotNullBeforeCheck`.
- `operation.WithoutConversions(includeExplicit: false)`: the operand inside implicit
  conversions; explicit casts and parentheses stay unless casts are requested. Null
  stays null.
- `method.Flow` (`MethodFlow`): `Graph`, `Data` (check `Succeeded`), `NullState(expression)`.

## String construction and collections

- `expression.AsBuiltString(expandConstants: false)` flattens string literals,
  concatenations and interpolation without evaluating holes. Constant references
  stay holes unless expansion is requested; unsupported string expressions stay opaque
  holes, and unresolved/non-string expressions return null. For strict string
  concatenation use `AsBuiltString(expandConstants: false, stringOperandsOnly: true)`;
  both arguments are required on this overload. Non-string concatenation operands
  are rejected, while interpolation holes are unaffected.
- `BuiltString`: `Parts`, `LiteralParts`, `StartsWith(holePredicate)` (first
  nonempty part is a matching hole). Parts expose
  `Kind`, `Text`, `Value`, `Location`, `Alignment`, `Format`, `FormattingSyntax` (the
  written alignment/format clauses with punctuation, or empty). `BuiltStringPartKind`
  distinguishes `Literal` from `Hole`. `CoalesceLiterals()`
  merges adjacent literal parts and keeps the first location; holes are unchanged.
- `expressions.WithCollectionExpressionTarget()` selects parameterless List, HashSet
  and Dictionary creations, rank-one arrays and `Array.Empty` calls in supported
  target-typed positions. Interface targets remain candidates even when the fix
  would change the concrete type and must be withheld.

## Projects and relationships

- `AnalysisProject`: `Name`, `TargetFramework`, `IsTestProject`, `Packages`,
  `ReferencesPackage(id)`, `Properties`, `HasType(type)`, `Compilation`, `Sources`, `Solution`.
- `candidate.Source.Solution` (or `project.Solution`) is the current analysis: read facts
  and queries in predicates and fixes with `fact.In(candidate.Source.Solution)`.
  In tests, each `Analyze()` creates a new analysis and updates project membership;
  `project.Solution` throws until the project belongs to one. During coverage planning,
  avoid coverage-dependent queries through this property.
- `solution.ProjectGraph`: `Includes(consumer, owner)`, `DependenciesOf`, `CompatibleViewsOf`.
- `solution.Relationships`: `CallsFrom`, `CallersOf`, `Reaches`, `DerivedTypes`, `FilesOf`.
- `ImplementationView` (`Interface`, `Projects`, `Implementations`): `IgnoringTestProjects()`,
  `ConcreteOnly()`, `WhereImplementation(...)`, `WithExactlyOneImplementation()`.
- `SourceBaseline(acceptedSolution)`: `ChangeOf(file)`, `ChangedFiles`, `PreviousAccessibility`.

## Coverage requirements

`Require(Coverage.Executed)`, `Require(Coverage.EnumerationStarted)`,
`Require(Coverage.Line.AtLeast(90))`, refined with `.OnUncovered(message)` and
`.OnUnknown(message)`. The engine runs the referencing tests and caches evidence.

## Example: counterpart and custom query

```csharp
using DrillPress;
using Microsoft.CodeAnalysis;

var rules = new RuleCatalog();
var codecs = Code.Types
    .InNonTestProjects()
    .ImplementingInterface(CodeType.Named("Shop.ITextCodec"))
    .Where(type => !type.IsAbstract && !type.IsInterface);

rules.Rule("TEAM020", "Add a <CodecName>RoundTrip test for this codec.")
    .For(codecs.WithoutMatching(Code.TestMethods, (codec, test) =>
        test.Name == $"{codec.Name}RoundTrip"))
    .Forbid();

var publicTypes = Code.Types.WithAccessibility(Accessibility.Public);
rules.Rule("TEAM021", "Give public types unique names across namespaces.")
    .For(CodeQuery<CodeTypeDefinition>.Create(solution => publicTypes.In(solution)
        .GroupBy(type => type.Name)
        .Where(group => group.Select(type => type.Namespace).Distinct().Count() > 1)
        .SelectMany(group => group)))
    .Forbid();
```

