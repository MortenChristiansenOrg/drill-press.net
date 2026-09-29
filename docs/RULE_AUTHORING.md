# Authoring compiled rules

For the reader-friendly HTML manual, open [Writing custom rules](../user-docs/index.html)
in a browser. It includes a guided first rule, searchable API coverage, and
light/dark themes. This page retains the repository's compiled-rule and preview
fix contracts.

A rule bundle constructs a `RuleSet` in ordinary C# and passes it to
`RuleApplication`. Rules are compiled into the managed or NativeAOT executable;
there is no runtime source compiler, assembly scan, or rule discovery.

For custom roots, cached facts, operations/flow, project requirements, baselines
and consumer fixtures, see the [composable SDK guide](SDK_CAPABILITIES.md).
The five preview rules below remain available alongside the configured codec
showcase in the sample bundle.

The SDK supplies analysis and edit primitives. Test-framework recognition,
layout conventions, and preferred spellings are implemented entirely in the
sample bundle, not shipped as SDK APIs.

```csharp
using DrillPress;

var rules = new RuleSet();
var legacyRead = CodeType.Named("Product.LegacyStore").Member("Read");
rules.For(legacyRead.References)
    .Forbid("TEAM001", "Use the application's current storage contract.");
```

`Code.Methods`, `Code.Types`, `Code.Interfaces`, and `Code.MemberReferences` share
one `AnalysisSolution`. Collections and semantic models are created on first
use. Each `AnalysisProject` is a separate evaluated compilation context;
frameworks and conditional source are not combined. Generated source supplies
compiler semantics and implementation counts but never reportable candidates.

`Where` returns a reusable selection. `RuleCondition<T>.And`, `.Or`, `.Not`, and
`.ExceptWhen` compose Boolean predicates with short-circuit evaluation.
`query.ExceptWhen(condition)` excludes exceptions. Predicates should be pure:
each query instance is materialized once per analysis. Derived queries share
custom-root discovery; different conditions remain independent selections.

```csharp
using DrillPress;

var shortName = new RuleCondition<CodeMethod>(method => method.Symbol?.Name.Length < 3);
var constructorLike = new RuleCondition<CodeMethod>(method => method.Symbol?.Name == "New");
var candidates = Code.Methods.Where(shortName.Or(constructorLike)).ExceptWhen(method => method.Source.Project.IsTestProject);
rules.For(candidates).Forbid("TEAM001", "Use a descriptive method name.");
```

`Forbid` reports every selected candidate. `Require` reports only candidates
that fail its condition. Both accept a location selector and an optional fix
factory. The selector runs only for violations. Without one, methods/types use
the declaration identifier and references use the complete bound expression.
`RuleDescriptor` can be reused with `Forbid`. IDs must be unique and nonblank;
IDs and messages must be single-line. Registration rejects invalid descriptors.

Use semantic identities instead of source spelling:

```csharp
var target = CodeType.Named("Product.Storage.Cache", "Product.Storage");
var reads = target.Member("Read").References;
var rent = CodeType.Named("System.Buffers.ArrayPool<>").Member("Rent");
var lists = CodeType.Of<List<string>>(); // includes the constructed string argument
```

`Named` accepts open generic slots: `List<>`, `Dictionary<,>`, and
`Outer<>.Inner<,>` normalize to CLR metadata names while matching any constructed
arguments. Dots after an open generic type denote nested types; use `+` to
distinguish a non-generic containing type from a namespace. Existing `+` and
backtick-and-arity names also work. Use `Of<T>()` for exact
constructed types; `Named("List<string>")` is not a C# type-expression parser.

Assembly qualification can be a simple name or full assembly display identity.
An omitted qualification matches any declaring assembly. Use `Members.Are` or
`CodeType.Matches` for selection; record equality does not apply optional-assembly
matching. Strongly typed BCL
identities recognize framework reference-assembly facades. `Members.Are` matches
the member's declaring symbol, including aliases, qualified access, and static
imports. Ambiguous candidate symbols are not guessed.

The sample bundle supplies these conventions:

| Rule | Reported location and behavior |
| --- | --- |
| DP1001 | Third whitespace-only physical line in a block-bodied xUnit test. Multiline strings/comments, disabled text, and nested functions do not supply blank lines. |
| DP1002 | First resolved xUnit assertion before the last qualifying blank line. A sole synchronous `Assert.Throws` is exempt; `ThrowsAsync` and multiple assertions are not. |
| DP1003 | One ordinary declaration of an interface with exactly one concrete non-test source implementation in a compatible evaluated view. Partial/generic definitions count once; inherited and generated implementations count. Source dependencies participate; external metadata consumers do not. |
| DP1004 | A resolved `string.Empty` reference. A fix replaces only the expression span with `""`, retaining surrounding trivia. |
| DP1005 | A resolved `StringComparer.Ordinal` reference within an argument. Only the documented overload pair below currently receives a fix. |

The sample bundle's xUnit selection recognizes Fact/Theory attributes in the xUnit core assemblies
and walks derived attribute base types. Assertions must resolve to xUnit's Assert
class. Similarly named unrelated attributes and assertion classes do not match.
Interface views combine compatible consumer roots for the same target framework;
alternate frameworks and incompatible dependency evaluations remain independent. A shared interface can consequently
violate the convention in one framework and satisfy it in another.

## Fix contracts

A fix factory returns `FixProposal`, containing every `SourceEdit` required by
one atomic correction and a context validation function. The engine invokes
that function for every loaded context containing an edited physical identity,
including contexts without a finding. Missing, inactive, generated, non-editable,
or ambiguously bound memberships cannot be assumed safe. The shared response
validator then requires agreement and withholds entire conflicting batches.
The `+` marker means that this complete plan survived validation; proposing a
fix does not write files.

The sample empty-string fix rejects `nameof`, expression trees, interior comments/directives, erroneous binding,
changed enclosing overloads, and changed conversions. In particular, converting
a nonconstant expression to a constant can change an overload selected several
expressions above the field reference. Rebinding only the immediate argument is
insufficient.

The sample comparer allowlist is exactly the BCL
`Enumerable.Distinct<string>(IEnumerable<string>, IEqualityComparer<string>)`
to `Enumerable.Distinct<string>(IEnumerable<string>)` pair. Static, extension,
and named arguments are mapped through compiler operations before removal;
the rewritten call and its enclosing expressions are rebound. Removed argument
trivia is preserved, which can leave an extra space. Other methods, constructors,
optional/params APIs, custom overloads, and expression-tree calls remain
non-fixable.

This equivalence follows from the [Distinct overload contract](https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.distinct?view=net-10.0)
and [ordinal string equality](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/strings/common-tasks/compare).
Ordering APIs can default to culture-sensitive comparison, and collection
constructors can expose comparer object identity, so successful rebinding alone
does not justify extending the allowlist.

### Declaration parts and reporting policies

The complete fluent authoring surface is available with `using DrillPress;`. This preview
consolidates the former authoring namespaces; update imports when upgrading. `Code` is
the discovery root for files, projects, calls, null checks, arbitrary syntax, semantic
types, methods and xUnit tests. Lower-level selection classes remain usable explicitly.

`Code.Types` identifies semantic type definitions once per compilation. Use
`Code.TypeDeclarations.TopLevel().WithExplicitModifier(Modifier.Internal)` to inspect
every written partial part's actual modifiers and physical path. Fixes on these
candidates target that part, not an arbitrary representative declaration.

`Code.LocalVariables` preserves local/for/using declaration groups. `Variables` exposes
all written declarators; `WithInitializer()` requires every declarator to have an
initializer and never splits a multi-variable declaration. `WithExplicitType()` is
semantic: a real type or alias named `var` is explicit. `Code.ForEachLoops` handles
ordinary iteration variables and `Code.OutVariables` handles out declarations;
deconstruction syntax remains available through `Code.Nodes<T>()`.

`Code.IfStatements.WithElse()` includes else-if chains, marked by `Else.IsElseIf`.
`BranchWithoutBraces` selects the unbraced side only when one ordinary branch has braces
and the other does not. `Fix.For(branch).AddBraces().Propose()` uses the existing
restricted block-wrapping proof.

```csharp
rules.For(Code.LocalVariables.WithExplicitType().WithInitializer())
    .Forbid("VAR", "Use var.", at: local => local.TypeName);

rules.For(Code.TestMethods.Body().ControlFlowNodes(ControlFlowKinds.AnyBranchOrLoop))
    .ReportOncePer(node => node.ContainingSymbol)
    .Forbid("BRANCH", "Remove conditional logic from test methods.");
```

`at:` accepts a source element, original syntax node/token, or physical source location.
It validates that the selection belongs to the candidate's compilation; synthetic syntax
is not a location. `AnyBranchOrLoop` includes if, switch statement/expression, conditional
expression, loops and catch filters, excluding `&&`, `||`, `??` and conditional access.
`ReportOncePer` chooses the first violation in file/span order within each compilation
and key. It evaluates every candidate and fix factory first. `RuleDiagnostic.Fixes`
retains every proposed correction; the engine still checks conflicts and compiles/proves
the combined edit set before exposing an atomic batch.

Requirements may put identity first with `Require("NAME", "Use the suffix.", when:
method => method.NameEndsWith("Async"))`. `NameStartsWith`, `NameEndsWith` and `NameMatches`
use ordinal/case-sensitive matching. `HasBody`, `HasEmptyBody` and `HasNoStatements`
distinguish missing bodies, empty blocks and expression bodies; an unresolved method
retains its syntax and has no fabricated `ContainingType`.
