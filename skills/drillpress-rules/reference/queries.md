# Queries and candidates

Everything is in `namespace DrillPress`. `CodeQuery<T>` is a lazy, reusable selection
cached per analysis; materialize one with `query.In(solution)` in tests.

## Roots (`Code.*`)

| Root | Yields | Notes |
| --- | --- | --- |
| `Files`, `FilesIncludingGenerated` | `CodeFile` | `Path`, `Name`, `Folder`; reports at 1:1 |
| `Projects` | `AnalysisProject` | one per evaluated context (per target framework) |
| `Types`, `Interfaces` | `CodeTypeDefinition` | partial types once per context |
| `TypeDeclarations` | `CodeTypeDeclaration` | every written part; `TopLevel()` |
| `Methods` | `CodeMethod` | ordinary methods, not constructors, accessors or local functions |
| `Fields` / `Properties` / `Parameters` | `CodeField` / `CodeProperty` / `CodeParameter` | one field candidate per variable; parameters include lambdas and primary constructors |
| `Declarations` | `CodeSymbol` | every declared symbol, including events, locals, accessors |
| `TestMethods`, `TestClasses`, `AbstractTestClasses` | `CodeMethod`, `CodeTypeDefinition` | xUnit, NUnit, MSTest markers incl. derived attributes |
| `Calls` | `CodeInvocation` | every bound call in bodies, initializers, attributes, top-level code |
| `ObjectCreations` | `CodeObjectCreation` | `new T(...)`, target-typed `new(...)` |
| `MemberReferences` | `MemberReference` | named references to fields, properties, methods (method groups, `nameof`) |
| `TypeReferences` | `CodeTypeReference` | every written use of a type; aliases resolved; `var` excluded |
| `ReferencesTo(symbol)` | `CodeSymbol` | references across compilations by identity |
| `IfStatements` | `CodeIfStatement` | `Then`, `Else`, `Branches`; `WithElse()`, `Branches().WithoutBraces()` |
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

## Shared filters

Every query of `ICodeElement` (anything reportable):

- `InProject(glob)`, `InTestProjects()`, `InNonTestProjects()`, `InProjectsWithType(type)`
- `InNamespace("A.B")`, `"A.*"` (one segment), `"A.**"` (A and descendants)
- `InFolder("Domain")` (whole segments at any depth), `InFilesNamed("*Tests.cs")`
- predicates: `element.IsInProject(glob)`, `IsInNamespace(pattern)`, `IsInFolder(folder)`

Every query of `ICodeDeclaration` (types, type parts, methods, fields, properties,
parameters, symbols):

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
  `IsRecord`, `IsAbstract`, `IsStatic`, `IsNested`, `Implements(type)`, `DerivesFrom(type)`,
  `IsOrDerivesFrom(type)`, `Symbol` (non-null), `Syntax`, `Solution`.
  Queries: `DerivedFrom(types)`, `ImplementingInterface(type)`, `ImplementationViews()`.
- `CodeTypeDeclaration`: `IsTopLevel`, `Syntax`, `Symbol`; attributes are those written on this part.
- `CodeMethod`: `IsAsync`, `IsStatic`, `ReturnsVoid`, `ReturnTypeIs(type)`, `Parameters`,
  `HasBody`, `HasEmptyBody`, `HasNoStatements`, `Body(nested?)`, `ContainingType`,
  `Flow`, `Reaches(member)` (static call paths through loaded source), `Syntax`.
  Queries: `DeclaredInTypesDerivedFrom(types)`, `Overriding(member)`,
  `DirectlyOverriding(member)`, `WhereBody(MethodBodyShape)`, `ContainingTypes()`.
- `CodeField`: `Type`, `TypeIs<T>()`, `IsConst`, `IsReadOnly`, `IsStatic`, `Initializer`, `Declaration`.
- `CodeProperty`: `Type`, `TypeIs<T>()`, `IsStatic`, `HasGetter`, `HasSetter`, `HasInit`,
  `IsAutoProperty`, `Initializer`.
- `CodeParameter`: `Type`, `TypeIs<T>()`, `Ordinal`, `ContainingSymbol`, `IsLambdaParameter`,
  `HasDefaultValue`, `DefaultValue`.

## Statements and syntax

- `CodeIfStatement`: `Then`, `Else`, `Branches`, `InconsistentlyBracedBranch` (unbraced side
  when only one side of an if/else has braces). `CodeBranch`: `HasBraces`, `IsElseIf`.
- `CodeCatch`: `ExceptionType` (null for `catch { }`), `Catches<T>()`, `CatchesAnyException`,
  `HasFilter`, `Filter`, `Body`, `IsEmpty`, `Rethrows`.
- `CodeVariableDeclaration`: `TypeName` (reportable), `HasExplicitType`, `HasInitializer`,
  `CanUseVar`, `Variables`; queries `WithExplicitType()`, `WithInitializer()`, `WhereVarPreservesType()`.
- `CodeNode<T>`: `Syntax`, `ContainingSymbol`, `Operation`, `TypeInfo`, `Constant`
  (`HasValue` first), `AsExpression()`; query `Duplicates(minimumTokens)` (repeated token shapes).
- `CodeBody` (`method.Body()`, `methods.Body()`): `Calls()`, `Calls(member)`, `Nodes<T>()`,
  `ControlFlowNodes(kinds)`, `NestedBodies()`, `IsEmpty`; `bodies.Conditions().Checks(pattern)`
  with `ConditionPattern.Null`, `EmptyString`, `NullOrEmptyString`, `NullOrWhiteSpaceString`,
  `ForCall(...)`; `bodies.NullChecks()`.

## Types and members

- `CodeType.Of<T>()`, `CodeType.Named("Ns.Outer+Inner")`, `Named("Ns.Map<,>")` (open
  generic), `Named(name, assemblyName)`, `CodeType.Framework("System.Linq.Enumerable")`
  (framework assembly only). `type.References` selects its `CodeTypeReference`s.
- `type.Member("Name")` (all overloads, fields, properties), `.WithParameters(types)`
  (one overload; none = parameterless), `type.Constructor(types)`.
- `member.References` (`MemberReference`: `Symbol`, `Expression`, `Facts`, `AsArgument()`);
  `references.OutsideNameOf()`.
- `ApiSet(members)`: `Contains(method)`, `Union(other)`; usable with `calls.To(set)`.
- `CodeTypeReference`: `Type`, `RefersTo<T>()`, `RefersTo(type)`.
- Raw symbols: `symbol.Attributes()` → `CodeAttribute` (`ConstructorValue<T>(name)`,
  `NamedValue<T>(name)`, `Matches(type)`); `Symbols.HasAttribute`, `IsOrDerivesFrom`,
  `DerivesFrom`, `Implements`; `symbol.NameMatches`, `IsInNamespace`.

## Calls, arguments and expressions

- `CodeInvocation`: `Target` (chosen overload), `Receiver`, `Arguments`,
  `Argument("name")` (bound, including omitted defaults; null for unknown names),
  `ArgumentsFor("name")`, `Calls(member)`, `IsDeclaredOn(type)`, `TargetNameMatches(glob)`,
  `IsConditional` (`?.`), `Expression`, `IsResolved`.
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
  `AsInvocation()`, `AsBuiltString()`.
- `CodeNullCheck`: `CheckedValue`, `Polarity`, `Domain`, `Form`, `IsKnownNotNullBeforeCheck`.
- `method.Flow` (`MethodFlow`): `Graph`, `Data` (check `Succeeded`), `NullState(expression)`.

## Projects and relationships

- `AnalysisProject`: `Name`, `TargetFramework`, `IsTestProject`, `Packages`,
  `ReferencesPackage(id)`, `Properties`, `HasType(type)`, `Compilation`, `Sources`.
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
