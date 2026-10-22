# Authoring compiled rules

For the reader-friendly HTML manual, open [Writing custom rules](../user-docs/index.html)
in a browser. It includes a guided first rule, searchable API coverage, and
light/dark themes. Coding agents can install the [rule-authoring skill](AGENT_SKILL.md)
for offline guidance. This page retains the repository's compiled-rule and fix
contracts.

A rule bundle constructs a `RuleCatalog` in ordinary C# and passes it to
`RuleApplication`. Rules are compiled into the managed or NativeAOT executable;
there is no runtime source compiler, assembly scan, or rule discovery.

For custom roots, cached facts, operations/flow, project requirements, baselines
and consumer fixtures, see the [composable SDK guide](SDK_CAPABILITIES.md).
Upgrading from an earlier preview? See the [API migration guide](API_MIGRATION.md).

The SDK supplies analysis and edit primitives, including recognition of xUnit,
NUnit and MSTest test methods. Layout conventions and preferred spellings are
implemented entirely in the sample bundle, not shipped as SDK policies.

## One declaration shape

Every rule has the same shape: an identity, one or more typed clauses, and an
obligation per clause.

```csharp
using DrillPress;

var rules = new RuleCatalog();
rules.Rule("TEAM001", "Use the application's current storage contract.")
    .For(CodeType.Named("Product.LegacyStore").Member("Read").References)
    .Forbid();
```

- `rules.Rule(id, message, fixComplexity?)` reserves a unique identity. IDs must
  be nonblank; IDs and messages must be single-line. Registration rejects invalid
  descriptors and duplicate IDs, even with identical messages. IDs starting with
  `drillpress-` (in any case) are reserved for [built-in rules](#allowing-individual-findings).
- `.For(query)` starts a clause over the selected candidates.
- `.Forbid(fix?)` reports every candidate. `.Require(predicate, fix?)` reports
  candidates for which the predicate is false.
- Optional clause modifiers come before the obligation:
  `.ReportAt(...)` chooses the reported span and `.ReportOncePer(key)` groups
  repeated findings.

`Forbid` and `Require` return the rule, so another clause can follow with
`.For(...)`. Evaluating a rule without any `Forbid` or `Require` clause throws, so
a forgotten obligation cannot silently disable a rule.

`Code` is the single discovery root: `Code.Files`, `Code.Projects`, `Code.Types`,
`Code.TypeDeclarations`, `Code.Methods`, `Code.Fields`, `Code.EnumMembers`, `Code.Properties`,
`Code.Parameters`, `Code.Calls`, `Code.ObjectCreations`, `Code.MemberReferences`,
`Code.TypeReferences`, `Code.NullChecks`, `Code.Catches`, `Code.Comments`,
`Code.TestMethods`, `Code.TestClasses`, and the statement roots. They share one
`AnalysisSolution`; collections and semantic models are created on first use.
Each `AnalysisProject` is a separate evaluated compilation context; frameworks
and conditional source are not combined. Generated source supplies compiler
semantics and implementation counts but never reportable candidates.

`Where` returns a reusable selection. `RuleCondition<T>.And`, `.Or`, `.Not`, and
`.ExceptWhen` compose Boolean predicates with short-circuit evaluation.
`query.ExceptWhen(predicate)` excludes exceptions. Predicates should be pure:
each query instance is materialized once per analysis. Derived queries share
custom-root discovery; different conditions remain independent selections.

```csharp
using DrillPress;

var rules = new RuleCatalog();
var shortName = new RuleCondition<CodeMethod>(method => method.Name.Length < 3);
var constructorLike = new RuleCondition<CodeMethod>(method => method.Name == "New");
rules.Rule("TEAM001", "Use a descriptive method name.")
    .For(Code.Methods.InNonTestProjects().Where(shortName.Or(constructorLike)))
    .Forbid();
```

### Shared declaration filters

Types, type parts, methods, fields, enum members, properties, parameters and declared
symbols implement `ICodeDeclaration`. The same filters and predicates apply to each:

| Filter on a query | Predicate on one declaration |
| --- | --- |
| `Named("A", "B")` | `Name == "A"` |
| `NameMatching("*Async")` | `NameMatches("*Async")`, `NameStartsWith`, `NameEndsWith` |
| `WithNameContainingAnyWord(["Should"])` | `NameContainsWord("Should")`, `NameWords` |
| `WithAttribute(type)` | `HasAttribute(type)`, `HasAttribute(type, where)`, `Attributes()` |
| `WithExplicitModifier(Modifier.Internal)` | `HasExplicitModifier(Modifier.Internal)` |
| `WithAccessibility(Accessibility.Public)` | `Accessibility` |
| | `HasDocumentationComment()` |

Name comparisons are ordinal and case-sensitive. Globs use `*` and `?`.
`CodeIdentifier.Words("HTTPClientFactory2")` returns `HTTP`, `Client`, `Factory`,
`2`; underscores, lower-to-upper and acronym boundaries, and letter/digit
transitions split words, and leading `@` is ignored. A written type part sees only
the attributes written in that part; a partial type definition combines the
modifiers and documentation of all parts.

Every source element also accepts the same scope filters: `InProject(glob)`,
`InTestProjects()`, `InNonTestProjects()`, `InProjectsWithType(type)`,
`InFolder(folder)`, `InFilesNamed(glob)` and `InNamespace(pattern)`. `Code.Projects`
accepts the project filters too: `InProject(glob)`, `InTestProjects()`,
`InNonTestProjects()` and `InProjectsWithType(type)`.

### Typed clauses under one identity

Reserve one identity when a policy covers several candidate types. Each clause
retains its own typed requirement and factories:

```csharp
var queryCoverage = rules.Rule("DATA002", "Exercise each data query.");
queryCoverage.For(Code.Calls.ToMethodsNamed("ReadQuery").Expressions())
    .Require(Coverage.Executed.OnUnknown("Inspect call execution evidence."));
queryCoverage.For(Code.Enumerations)
    .Require(Coverage.EnumerationStarted.OnUnknown("Inspect loop advancement evidence."));
```

`Rule(descriptor)` also accepts a configured `RuleDescriptor`; optional fix
complexity belongs to the shared descriptor. Built-in requirements keep their
evidence, outcome remediation and gating policy. Coverage planning unions the
contexts selected by all clauses; empty selections do not launch tests unless
`CollectCoverageIn` requests them.

`ReportOncePer` groups within its own clause and compilation; equal keys in
different clauses do not group them together. Direct `RuleCatalog.Evaluate` returns
each clause's diagnostic. The production response validator combines findings
with the same rule ID and physical span, including linked compilation memberships:
evidence and remediation are combined, typed facts are retained, and any violation
keeps the combined finding gating. A shared ID alone does not merge different spans.

Fixes from all clauses, including hidden reporting-group candidates, pass the
existing conflict and combined-compilation checks. Identical proposals can share
a batch; conflicting or jointly invalid proposals are withheld. A deduplicated
occurrence receives a fix only when every contributing finding agrees on the
validated batch. Display grouping never bypasses those checks.

### Reporting locations

Without `ReportAt`, methods, types, fields, properties and parameters report at
their identifier, calls at the written method name (`ThenBy` in
`items.OrderBy(a).ThenBy(b)`, so calls in one chain never share a location),
references at the complete expression, and catch clauses at their header. Use
`ReportAt(call => call.Operation.Syntax)` to report a call's complete expression. `ReportAt` accepts an element in the candidate's own
compilation, an original syntax node or token, or a physical `SourceLocation`.
Returning null keeps the candidate's own location. Candidates without a location,
such as tuples from `Select` or `Join`, must report at an element:

```csharp
rules.Rule("SDK2007", "Keep reader and writer format inventories in agreement.")
    .For(readers.Join(writers, reader => reader.Source.Project, writer => writer.Source.Project,
            (reader, writer) => (Reader: reader, Writer: writer))
        .Where(pair => !HaveTheSameFormats(pair.Reader, pair.Writer)))
    .ReportAt(pair => pair.Reader)
    .Forbid();
```

A tuple resolves syntax, tokens and `SourceLocation`s in the compilation of its first
code element (searching nested tuples), so `.ReportAt(pair => pair.Reader.Syntax.Identifier)`
also works. Other candidate types without a compilation, such as a record that does not
implement `ICodeElement`, are rejected when the clause is registered: `ReportAt` with
syntax, a token or a location throws, and so does `Forbid` or `Require` without
`ReportAt`. Synthetic syntax and elements from another compilation are rejected.

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
`Framework(name)` matches only signed framework runtime and reference assemblies,
so source-defined lookalikes never match.

Assembly qualification can be a simple name or full assembly display identity.
An omitted qualification matches any declaring assembly. Use `CodeType.Matches`
for selection; record equality does not apply optional-assembly matching. Strongly
typed BCL identities recognize framework reference-assembly facades.
`CodeMember.References` matches the member's declaring symbol, including aliases,
qualified access, and static imports; `CodeType.References` matches every written
type reference, including aliases, attributes, qualifiers and generic arguments.
Ambiguous candidate symbols are not guessed.

The sample bundle supplies these conventions:

| Rule | Reported location and behavior |
| --- | --- |
| DP1001 | Third whitespace-only physical line in a block-bodied test. Multiline strings/comments, disabled text, and nested functions do not supply blank lines. |
| DP1002 | First resolved xUnit assertion before the last qualifying blank line. A sole synchronous `Assert.Throws` is exempt; `ThrowsAsync` and multiple assertions are not. |
| DP1003 | An interface with exactly one concrete non-test source implementation in every compatible evaluated view. Partial/generic definitions count once; inherited and generated implementations count. Source dependencies participate; external metadata consumers do not. |
| DP1004 | A resolved `string.Empty` reference outside `nameof`. A fix replaces only the expression span with `""`, retaining surrounding trivia. |
| DP1005 | A resolved `StringComparer.Ordinal` reference within an argument. Only the documented overload pair below currently receives a fix. |

The sample bundle selects tests with `Code.TestMethods`, which recognizes xUnit,
NUnit and MSTest markers, including derived marker attributes and markers
inherited by overrides. Assertions must resolve to xUnit's Assert class.
Interface views combine compatible consumer roots for the same target framework;
alternate frameworks and incompatible dependency evaluations remain separate
views, and DP1003 requires exactly one implementation in each of them.

## Rule fix complexity

Estimate how much context and design judgment an agent needs for a typical fix
with the optional `fixComplexity` argument on `Rule`:

```csharp
rules.Rule("EMPTY", "Use an empty literal.", fixComplexity: RuleFixComplexity.Trivial)
    .For(CodeType.Of<string>().Member(nameof(string.Empty)).References)
    .Forbid();
```

Reusable descriptors support the same metadata:

```csharp
var descriptor = new RuleDescriptor("EMPTY", "Use an empty literal.")
{
    FixComplexity = RuleFixComplexity.Trivial,
};
rules.Rule(descriptor)
    .For(CodeType.Of<string>().Member(nameof(string.Empty)).References)
    .Forbid();
```

`RuleFixComplexity` is available with `using DrillPress;`. Its enum members have
XML documentation describing these four levels:

| Level | Context and judgment needed |
| --- | --- |
| `Trivial` | An isolated mechanical edit, typically one line, with no surrounding code or design decisions. |
| `Local` | Nearby code or a small set of related symbols; a clear correction with little design judgment. |
| `Complex` | Behavioral reasoning across related code and meaningful judgment, such as adding useful method test coverage. |
| `Architectural` | Broad context and substantial judgment about responsibilities, abstractions, or component dependencies. |

These are author estimates of agent effort, independent of severity, detection
cost, or automatic fix availability. Line and file counts alone do not determine
the level. Omit metadata when no estimate is available; null means unclassified,
and is not equivalent to `Trivial`.

The CLI hides complexity by default. `--show-fix-complexity` adds an annotation
only for classified rules, once in the rule header:

```text
EMPTY [fix:trivial] Use an empty literal.
Example.cs
  7:20
```

Use `--fix-complexity <levels>` independently of the display option to route
findings to models with different capabilities. For example, send the first
command's output to a model suited to bounded edits and the second to a model
suited to reasoning and design:

```sh
drillpress check --rules Rules.dll Target.csproj --fix-complexity trivial,local --show-fix-complexity
drillpress check --rules Rules.dll Target.csproj --fix-complexity complex,architectural --show-fix-complexity
```

Run `--fix-complexity unspecified` separately to triage unclassified rules, or
include `unspecified` in any selection. Specify the option once with a
comma-separated list; names are case-insensitive and surrounding spaces are
ignored. Unknown or empty values fail with exit code 2.

`drillpress status` takes the same options and counts violations per project and
fix complexity, which shows how much work each selection would route.

Without a filter all rules are included. With a filter `check`, `fix` and `status` report
only selected findings, and only selected violations cause exit code 1. A result
without selected violations exits 0, including visible review-only findings,
even if excluded rules have violations. `fix` applies only
selected safe batches and rechecks with the same selection. All findings and
fixes are validated before selection: filtering cannot hide malformed responses,
remove conflicts, or bypass cross-context agreement. Atomic batches shared with
an excluded rule are withheld in full, so a selected finding can remain unfixed.
This selection controls reporting and edits; all rules are still evaluated.

## Allowing individual findings

Where a convention does not fit, the code can accept a finding with an ordinary
comment that names the rule and says why:

```csharp
var page = orders.Skip(offset); // drillpress-allow BR7-skip: the export pages a fixed snapshot

// drillpress-allow BR7-skip, BO1-clock: the importer replays recorded batches
public void Replay(IReadOnlyList<Batch> batches)
{
    // ...
}
```

The comment is `// drillpress-allow`, one or more rule IDs separated by commas,
a colon, and a non-empty reason. IDs are trimmed and compared ordinally, so an ID
containing a comma or colon cannot be allowed. Documentation comments, `/* */`
comments, strings and inactive `#if` code are never allowances, nor is a comment
whose marker continues with other text, such as `// drillpress-allowed`.

The scope depends on where the comment stands:

- After code on the same line, it allows findings that start on that line, the
  line printed in the output.
- On its own line, it applies to the next code line. Only further non-blank
  lines, such as other comments, may come between; a blank line makes the
  allowance invalid. When the next code line starts a statement or a member or
  type declaration, the allowance covers the whole outermost one starting there,
  including its attributes, so it can allow a multi-line statement or a whole
  test method. A comment between attributes and the declaration also covers the
  declaration. Namespaces are never a scope. Otherwise it allows findings that
  start on that code line, such as one operand of a multi-line expression.

A finding is allowed when its reported location starts within the scope of a
valid allowance that names its rule. When several allowances cover a finding,
the one with the narrowest scope allows it. An allowed finding is removed before
fixes are planned: it is not printed or counted by `status`, does not affect
the exit code, and its fix is never applied. `AnalysisEngine`, the CLI and
`RuleTestWorkspace.CheckAsync` apply allowances; `RuleCatalog.Evaluate` returns
every diagnostic.

Allowances are checked so they cannot rot. Two built-in rules report them:

| Rule | Complexity | Reported when |
| --- | --- | --- |
| `drillpress-allow-unused` | `Trivial` | A named rule reports no finding in the allowance's scope in any evaluated context. The evidence lists those rules, and a fix removes them from the comment, or removes the comment (its whole line when it stands alone) when it names no other rule. |
| `drillpress-allow-invalid` | `Local` | The comment has no rule ID or reason, names a rule the bundle does not define, or has no code directly below it. The evidence says which; an invalid allowance allows nothing and has no fix. |

```text
drillpress-allow-unused Remove the listed rules from this allowance; they report nothing in its scope.
src/Export.cs
  +12:37 [BR7-skip]
drillpress-allow-invalid Write allowances as // drillpress-allow RULE-ID[, RULE-ID]: reason, after or directly above the code.
src/Import.cs
  40:5 [unknown rule BR7-skp]
```

Use is decided across all contexts: an allowance in a linked file or a
multi-targeted project is used when any context reports an allowed finding, as
with code that only one target framework compiles. Rules that a run does not
evaluate never make an allowance unused: `--skip-coverage` skips coverage rules,
and `--fix-complexity` only filters what is reported after every rule ran.
Only analysis-target documents are read, so allowances in referenced projects
outside the target and in generated files are ignored. Built-in rules cannot be
allowed. Fix complexity selection applies to the built-in findings like any
other rule's, and `fix` removes unused allowances that pass the usual comment
removal checks; removing a whole line is withheld when it would change a later
`CallerLineNumber` value.


## Fix contracts

A fix factory returns `FixProposal?`. Start with `Fix.For(element)`, choose an
edit, and end the chain with `Propose()` when the library owns the equivalence
proof or `SafeWhen(change => ...)` when you state why the edit preserves behavior.

| Starting point | Edits | Ending |
| --- | --- | --- |
| `Fix.For(expression / reference / creation)` | `ReplaceWith("{0} == {1}", left, right)`, `ReplaceWithLiteral(value)`, `ReplaceWithEquality(left, right)` | `SafeWhen` |
| `Fix.For(call)` | the expression edits, `RemoveArgument(parameter)` | `SafeWhen` |
| `Fix.For(argument)` | `Remove()` | `SafeWhen` |
| `Fix.For(declaration)` | `RemoveModifier(Modifier.X)` | `Propose()` for accessibility tokens that keep the declared accessibility, otherwise `SafeWhen` |
| `Fix.For(branch)` | `AddBraces()` | `Propose()` |
| `Fix.For(comment)` | `Remove()`, `Remove(preserveLines: true)` | `Propose()` |
| `Fix.For(variable)` | `UseVar()` | `Propose()` |
| `Fix.Extract(group)` | `ToConstant(name)`, `ToMethod(name, parameter)` | `Propose()` for constants, otherwise `SafeWhen` |

The proposal contains every `SourceEdit` required by one atomic correction and a
context validation function. The engine invokes that function for every loaded
context containing an edited physical identity, including contexts without a
finding. Missing, inactive, generated, non-editable, or ambiguously bound
memberships cannot be assumed safe. The shared response validator then requires
agreement and withholds entire conflicting batches. The `+` marker means that
this complete plan survived validation; proposing a fix does not write files.

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
`Code.IfStatements.Branches().WithoutBraces()` selects every unbraced branch except
else-if continuations. `InconsistentlyBracedBranch` selects the unbraced side only when one ordinary branch has braces
and the other does not. `Fix.For(branch).AddBraces().Propose()` uses the
restricted block-wrapping proof.

```csharp
rules.Rule("VAR", "Use var.")
    .For(Code.LocalVariables.WhereVarPreservesType())
    .ReportAt(local => local.TypeName)
    .Forbid(fix: local => Fix.For(local).UseVar().Propose());

rules.Rule("BRANCH", "Remove conditional logic from test methods.")
    .For(Code.TestMethods.Body().ControlFlowNodes(ControlFlowKinds.AnyBranchOrLoop))
    .ReportOncePer(node => node.ContainingSymbol)
    .Forbid();
```

`AnyBranchOrLoop` includes if, switch statement/expression, conditional
expression, loops and catch filters, excluding `&&`, `||`, `??` and conditional access.
`ReportOncePer` chooses the first violation in file/span order within each compilation
and key. It evaluates every candidate and fix factory first. `RuleDiagnostic.Fixes`
retains every proposed correction; the engine still checks conflicts and compiles/proves
the combined edit set before exposing an atomic batch.

`HasBody`, `HasEmptyBody` and `HasNoStatements` distinguish missing bodies, empty
blocks and expression bodies; an unresolved method retains its syntax and has no
fabricated `ContainingType`.

`CanUseVar` and `WhereVarPreservesType()` apply to local, foreach and out
declarations. They rebind the replacement in its original context and require the
same variable type, nullable annotations, tuple element names and enclosing call
binding. Target-typed values, multi-declarator locals, conversions and unresolved
code are excluded. C# may infer a nullable reference annotation for `var` even
from a non-null initializer, so changing `string value = "x"` is excluded while
`string? value = "x"` can qualify. `UseVar()` replaces only the type span and
validates the final edit batch in every affected context.

### Comments

```csharp
rules.Rule("PHASE", "Remove test phase labels.")
    .For(Code.TestMethods.Body().Comments()
        .Where(comment => phaseLabels.Contains(comment.Text.Trim())))
    .Forbid(fix: comment => Fix.For(comment).Remove().Propose());
```

`Code.Comments` selects every comment; `Comments()` also works on files, methods,
type declarations, bodies and syntax-node queries. Candidates have a typed `Kind`,
delimiter-free `Text`, original `Source`, their own `Location`, and the nearest
declaration owning the trivia's token. Declaration queries include their exterior
trivia; body queries retain comments inside the executable body, including after
an expression-body arrow, and honor `NestedFunctions`. Overlapping scopes
deduplicate candidates per source membership. Strings and inactive preprocessor
text contribute no comments. Documentation text retains whitespace and line
endings after removing each `///` prefix or the outer `/** */` delimiters.

`Remove()` deletes the full physical line range when a comment stands alone,
including its final line break, while retaining adjacent blank lines. Inline
removal keeps a separator when needed. Token correspondence, compilation and
compiler-supplied argument checks withhold changes that alter parsing or caller
information, such as subsequent `CallerLineNumber` values.

`element.ExplanatoryComment()` (or `source.ExplanatoryComment(position)` for a token
inside the element) finds the ordinary comment that explains a line: a `//` or `/* */`
comment on it, on the comment line directly above it, or directly above the statement
or member that contains it. Documentation comments and `drillpress-allow` comments do
not count, and a blank line separates a comment from the code below it.
`HasExplanatoryComment()` tests for one, for rules such as "say why this `!` is safe".

Use `Remove(preserveLines: true)` to retain every original line break while
deleting the comment text, including multiline comments. Standalone comment lines
become empty lines. The same safety checks still apply in every affected loaded
context; changed `CallerArgumentExpression` text still withholds the fix.
