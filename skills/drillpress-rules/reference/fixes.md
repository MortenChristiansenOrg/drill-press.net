# Fixes

A fix factory receives the violating candidate and returns `FixProposal?`. Null keeps
the finding without a fix; `FixProposal.Withheld("reason")` does too and records why.
Builders may return null or a withheld proposal for unsupported source shapes.
`drillpress check --explain-fixes` (or `result.ExplainedOutput` in tests) prints the
recorded reason, or the check that rejected the fix during validation, under the finding.
A null proposal has no explanation; `Withheld` requires a nonblank, single-line reason without control characters such as tabs.
Start at `Fix.For(candidate)` (or `Fix.Extract(group)`) and finish with
`Propose()` or `SafeWhen(proof)`, except `AddArgumentOfType`, which finishes directly.

## Builders

| `Fix.For(...)` of | Edit | Finish |
| --- | --- | --- |
| `CodeExpression`, `MemberReference`, `CodeObjectCreation`, `CodeInvocation` | `ReplaceWithLiteral(value)`, `ReplaceWith("{0} == {1}", a, b)`, `ReplaceWithEquality(a, b, absorbNegation: true)`, `ReplaceWithReference(symbol)` (shortest spelling that binds), `ReplaceWith(syntax)` | `SafeWhen` |
| `CodeInvocation` / `CodeArgument` | `RemoveArgument("name")` / `Remove()` | `SafeWhen` |
| `CodeInvocation` | `AddArgument("name", "expression")` → `ArgumentAddition`, optionally `.ExpectOverloadChange(from, to)` or `.ExpectTransition(transition)` | `SafeWhen` |
| any `ICodeDeclaration` | `RemoveModifier(Modifier.X)` | `Propose()` for accessibility that stays the same, else `SafeWhen` |
| any `ICodeDeclaration` | `AddModifier(Modifier.X)` → `ModifierAddition` | `Propose()` for `sealed`/`static` classes (checks the whole loaded solution), else `SafeWhen` |
| any `ICodeDeclaration` (`Code.Declarations` includes locals) | `Rename("newName")` → `SymbolRename`: declaration, references, named arguments, `nameof`, `param`/`paramref` | `Propose()` for locals, local functions, their parameters, private-method parameters and private members, else `SafeWhen(change => ...)`; withheld across project references, when rebinding detects capture/shadowing or a string equals the old name |
| expression candidates | `UseCollectionExpression()` → `CollectionExpressionConversion`; `UseTimeProvider(clock)` → `TimeProviderReplacement` | `Propose()` |
| expression candidates (object creation) | `AddInitializer(member, value)` → `InitializerAssignment` | `SafeWhen(value => ...)` |
| declarations (automatic property) | `UseInitOnly()` → `InitOnlyConversion` | `SafeWhen(propertySymbol => ...)` |
| `CodeNode<NamespaceDeclarationSyntax>` (`NamespaceFix`) | `UseFileScopedNamespace()` → `FileScopedNamespaceConversion` | `Propose()` |
| `CodeNode<TStatementSyntax>` (`StatementSyntax` subtype; `StatementFix`) | `UseAssertAll()` → `AssertionLoopConversion` | `Propose()` |
| `CodeInvocation` | `AddArgumentOfType(type, expression, proof)` | returns a proposal directly |
| `CodeBranch` (`Code.IfStatements.Branches()`, `Code.EmbeddedStatements`) | `AddBraces()` | `Propose()` |
| `CodeComment` | `Remove()`, `Remove(preserveLines: true)` | `Propose()` |
| `CodeVariableDeclaration` | `UseVar()` | `Propose()` |
| `ExpressionGroup` via `Fix.Extract(group)` | `ToConstant(name)`, `ToMethod(name, parameterName)` | `Propose()` (constants), `SafeWhen` (templates) |

All proposals check editable ordinary source, inactive text and compilation. Each
builder adds its own trivia and binding gates: expression replacement, for example,
rejects `nameof`, expression trees and interior comments, and preserves surrounding
bindings, kept operands and caller info. These checks do not prove arbitrary behavior
unchanged. The engine validates the whole batch in every affected compilation,
including linked files and other target frameworks.

## Expression replacement

```csharp
using DrillPress;

var rules = new RuleCatalog();
var equals = CodeType.Of<string>().Member(nameof(string.Equals))
    .WithParameters(CodeType.Of<string>(), CodeType.Of<string>());

rules.Rule("TEAM030", "Use == for ordinal string equality.")
    .For(Code.Calls.To(equals))
    .Forbid(fix: call => call.Argument("a")?.Value is { } left
        && call.Argument("b")?.Value is { } right
            ? Fix.For(call)
                .ReplaceWithEquality(left, right, absorbNegation: true)
                .MustPreserve(ExpressionBehavior.EvaluationCounts | ExpressionBehavior.EvaluationOrder)
                .SafeWhen(change => change.Kept.All(operand => operand.Before.TypeIs<string>()))
            : null);
```

- Template holes `{0}`, `{1}` embed the passed original expressions unchanged;
  parentheses are added only where precedence requires them.
- `absorbNegation: true` turns `!string.Equals(a, b)` into `a != b`.
- The proof receives `ExpressionChange`: `Before` and `After` are `CodeExpression`s
  bound to the original and rewritten compilations; `Kept` lists each kept operand with
  its uses after the edit; `Rewrite` exposes raw syntax and semantic models.
- Compare symbols from `Before` and `After` with `change.Same(before, after)`.
  `SymbolEqualityComparer` does not equate source declarations across compilations.
  `RewriteContext.Same` is also available in custom proofs, and
  `ArgumentAdditionChange.Same` in argument additions. Namespace symbols compare by
  qualified identity; source declarations must map to their rewritten counterparts.
- `MustPreserve(ExpressionBehavior.EvaluationCounts)` requires each kept operand to run
  exactly once; `EvaluationOrder` keeps straight-line order; `NullReceiverBehavior`
  requires a receiver whose null check would move to be intrinsically non-null.
- The replacement keeps the converted type of the replaced expression. Target-typed
  replacements such as `default` → `CancellationToken.None` or `new List<string>()`
  → `[]` work when the original already has the destination's type; a discarded
  statement value (`x?.Log();` → `Track(x);`) may change type. Inferred tuple element
  names may change only when the tuple converts to the same named tuple type.
- `ReplaceWithReference(symbol)` accepts an original field, property, enum member,
  constant, local, parameter or type. It tries bare and increasingly qualified names
  through `global::`, and checks binding again in every context. Methods, aliases
  and preprocessing symbols throw; an unbindable reference withholds the fix.
- Prove the policy fact only you know (that the methods are equivalent). Compiler
  success alone is not proof.

## Argument removal

```csharp
using DrillPress;
using Microsoft.CodeAnalysis;

var rules = new RuleCatalog();
var distinct = CodeType.Framework("System.Linq.Enumerable").Member("Distinct");
var sequence = CodeType.Framework("System.Collections.Generic.IEnumerable<>");
var comparer = CodeType.Framework("System.Collections.Generic.IEqualityComparer<>");
var withComparer = distinct.WithParameters(sequence, comparer);
var ordinal = CodeType.Of<StringComparer>().Member(nameof(StringComparer.Ordinal));

rules.Rule("TEAM031", "Omit StringComparer.Ordinal; strings already compare ordinally.")
    .For(Code.Calls.To(withComparer)
        .WhereArgument("comparer", argument => argument.Value?.RefersTo(ordinal) == true))
    .Forbid(fix: call => Fix.For(call).RemoveArgument("comparer")
        .ExpectOverloadChange(from: withComparer, to: distinct.WithParameters(sequence))
        .RequireRemovedValue(change => change.RemovedValue?.RefersTo(ordinal) == true)
        // Ordinal is an immutable singleton, so skipping its getter is unobservable.
        .RequireRemovedEvaluation(ComparesStrings)
        .SafeWhen(ComparesStrings));

static bool ComparesStrings(ArgumentRemovalChange change) =>
    change.Expected.Before.TypeArguments is [{ SpecialType: SpecialType.System_String }];
```

- `ExpectOverloadChange(from, to)` declares the overload the call must bind to after the
  edit; parameter maps are inferred when names and types line up, otherwise use
  `ExpectTransition(new MethodTransition(...))`.
- `RequireRemovedValue` proves the removed value equals the default behavior;
  `RequireRemovedEvaluation` approves losing its evaluation (getter, conversion,
  initialization). Both are mandatory. `RequireSynthesizedArguments` approves changed
  caller-info arguments.
- Omitted values, receivers and `params` arguments are not removable.

`AddArgument("token", "TestContext.Current.CancellationToken")` supplies an omitted
optional parameter of the same overload; add `.ExpectOverloadChange(from, to)` when the
call moves to another overload (`StartsWith(x)` → `StartsWith(x, StringComparison.Ordinal)`).
The proof over `ArgumentAdditionChange` states why the value is right
(`change.AddedValue.RefersTo(ordinal)`); an intended behavior change, such as invariant
culture, is stated there too. `ArgumentAdditionChange` exposes `Before`, `After`,
`AddedValue`, `Rewrite`, `Expected` (null without an overload transition) and `Same`.
An existing explicit value or expanded `params` withholds the addition. The inserted
argument is positional when it follows the written parameters, otherwise named.
Each addition proves its own changed default in an atomic batch; other defaults and
caller-info arguments remain protected.

```csharp
using DrillPress;

var rules = new RuleCatalog();
var startsWith = CodeType.Of<string>().Member(nameof(string.StartsWith));
var original = startsWith.WithParameters(CodeType.Of<string>());
var explicitComparison = startsWith.WithParameters(
    CodeType.Of<string>(), CodeType.Of<StringComparison>());
var ordinal = CodeType.Of<StringComparison>().Member(nameof(StringComparison.Ordinal));

rules.Rule("TEAM036", "Use ordinal prefix comparisons for application keys.")
    .For(Code.Calls.To(original).InNamespace("Shop.Keys.**"))
    .Forbid(fix: call => Fix.For(call)
        .AddArgument("comparisonType", "global::System.StringComparison.Ordinal")
        .ExpectOverloadChange(original, explicitComparison)
        .SafeWhen(change => change.AddedValue.RefersTo(ordinal)));
```

The rule owns the policy that keys compare ordinally; the builder proves the
declared overload transition and preservation of retained inputs.

## Modifiers, braces, comments and var

Comment removal deletes standalone lines by default. Use `Remove(preserveLines: true)`
to keep every line break, including those inside multiline comments, when caller
line numbers of calls matter. Parsing and compiler-supplied arguments must still be unchanged
in every affected loaded context; caller argument text can still prevent a fix. An
attribute's caller line number and file path, such as those of xUnit's `[Fact]`, are
not compared, so edits above test methods stay fixable.

```csharp
using DrillPress;

var rules = new RuleCatalog();

rules.Rule("TEAM032", "Omit the default private modifier.")
    .For(Code.Fields.WithExplicitModifier(Modifier.Private))
    .ReportAt(field => field.ExplicitModifier(Modifier.Private))
    .Forbid(fix: field => Fix.For(field).RemoveModifier(Modifier.Private).Propose());

rules.Rule("TEAM033", "Track TODOs in the issue tracker instead of comments.")
    .For(Code.Comments.Where(comment => comment.Text.Contains("TODO")))
    .Forbid(fix: comment => Fix.For(comment).Remove().Propose());

rules.Rule("TEAM034", "Use var when the type is apparent.")
    .For(Code.LocalVariables.WithExplicitType().WithInitializer().WhereVarPreservesType())
    .Forbid(fix: declaration => Fix.For(declaration).UseVar().Propose());
```

`RemoveModifier(...).Propose()` succeeds only for accessibility keywords whose removal
keeps declared accessibility, identity and contract. For other modifiers, add
`MustPreserve(DeclarationBehavior.Identity | DeclarationBehavior.Contract)` and a
`SafeWhen(change => ...)` proof over `ModifierChange` (`Symbols` maps before/after).

`AddModifier(...).Propose()` proves only `sealed` and `static` classes: `sealed`
requires no loaded derived type and a nonabstract, nonstatic class; `static` requires
only static members, no written constructor, object as the base, no interfaces or
loaded derived types, and no loaded type use beyond member access, `nameof` or
`using static`. Both assume the loaded solution contains every consumer, including
reflection, dependency injection and mocking proxies. Other additions need
`MustPreserve(DeclarationBehavior...)` as appropriate and `SafeWhen`.
`ModifierChange` exposes `Added`, `AddedModifier`, `Removed`, `RemovedModifier`,
`Symbols` and `Rewrite`; `static` replaces a written `sealed` token.

`Rename(newName)` also updates named arguments, `nameof` and documentation
`param`/`paramref`, escaping keywords with `@`. Virtual, abstract, overriding,
interface-implementing, extern, partial and entry-point methods, lambda parameters
and record parameters are withheld. Wider renames need `SafeWhen(RenameChange)`,
whose `Context`, `Before`, `After` and `References` (count) let the consumer approve
reflection, serialization, model binding and unloaded consumers. Generated or
non-editable references, cross-project references, capture/shadowing (including in the
other files of a partial type), string literals of any kind equal to the old name,
inactive `#if` code naming either name or changed caller info withhold even an approved
wider rename.

## Extraction of repeated expressions

```csharp
using DrillPress;

var rules = new RuleCatalog();
var literals = Code.Nodes<Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax>()
    .Expressions()
    .Where(expression => expression.TextValue is { Length: > 8 });

rules.Rule("TEAM035", "Extract repeated string constants.")
    .For(literals.ConstantGroups())
    .Forbid(fix: group => Fix.Extract(group).ToConstant("SharedValue").Propose());
```

`ConstantGroups()` and `TemplateGroups(options)` group only the expressions you
selected, per owning type. Template extraction needs `SafeWhen` over `ExtractionChange`.
Its `Occurrences` expose `Before`, `After` and `Kept` as semantic expressions in
their actual original/rewritten contexts; `occurrence.Rewrite` keeps raw compiler evidence.

See [TemplateExtractionRules.cs](../examples/TemplateExtractionRules.cs) and its
[tests](../examples/TemplateExtractionRulesTests.cs) for a complete Boolean proof.
It accepts only ordinary interpolation of one string parameter, without alignment,
format clauses or moved calls. Reading that parameter once at the original site
and interpolating its string value needs no culture-dependent formatting.
Alignment/format cases still report the finding but offer no fix.

The builder proves template/capture correspondence, unchanged bindings and caller
information in every affected loaded context, and compilation of the full batch.
The example's consumer contract observes string contents, not allocation identity/count,
resource exhaustion or stack inspection. Those assumptions cannot be inferred from syntax.
Broader policies must also justify evaluation timing, culture/formatting and configured-call
effects; allowlisting a call establishes none of these.

## Custom edits

When no builder fits, build raw edits and prove the whole batch yourself:

```csharp
using DrillPress;
using Microsoft.CodeAnalysis.Text;

static FixProposal ReplaceSpan(AnalysisSource source, TextSpan span, string text,
    Func<RewriteContext, bool> proof) =>
    SourceChanges.Propose([SourceChanges.Replace(source, span, text)], proof);
```

The proof receives `RewriteContext` (`Original`, `Rewritten` compilation with the whole
batch applied, `Edits`, `Map(source, originalNode)`); inspect it rather than re-parsing.
`RewriteChecks.SameEnclosingBindings` enforces the converted-root, discarded-value
and tuple-name rules described above. `RewriteChecks.SameSourceBindings` checks only
edited member bodies when declarations and body boundaries stay unchanged, otherwise
the compilation. Expressions changed
by another proposal in the batch remain that proposal's responsibility.
Custom edits do not get the builders' trivia and binding checks. Edits must not
overlap; two replacements may share a boundary, but an insertion (empty span) may not
touch another edit, so fold it into the adjacent replacement. The same rule applies
within a proposal and across batches. A raw `new FixProposal(edits, validate,
validateCombined: ...)` must provide the combined validator to coexist with other
edits in that compilation; without it only its exact own batch is accepted.
`Edits` is empty and `IsSafeIn` false for a withheld proposal; `WithheldReason`
records a factory's reason before validation.

## Testing fixes

Assert `result.Output` (a validated fix shows as `  +5:32`, `+` directly before the
line number) and the complete
`result.FixedText(path)`. Add a case where the proof fails and assert the finding has
no `+` and `FixedText` returns the original text.

## Reusable structural and framework edits

- `Fix.For(expression).UseCollectionExpression().Propose()` converts supported standard
  collections/arrays with a written target type, preserving the concrete type and each
  element's evaluation count and order. Nonempty dictionary initializers, capacity
  constructors and interface targets changing concrete type are withheld.
- `Fix.For(namespaceNode).UseFileScopedNamespace().Propose()` converts a file's single
  block namespace and removes one four-space indentation level. Nested namespaces,
  directives, multiline text and ambiguous layouts are withheld, and so is a conversion
  that would change a string's value or a compiler-supplied argument such as a call's
  caller line number. A consumer must exclude
  source exported or concatenated into a different compilation.
- `Fix.For(call).AddArgumentOfType(type, expression, proof)` finds the unique overload
  adding that type and uses the argument-addition validator. Existing parameter names,
  types, ref kinds and generic arity must match, comparing generic overloads with the call's
  type arguments; the supplied proof explains the new value.
- `Fix.For(expression).UseTimeProvider(clockExpression).Propose()` supports `DateTime.UtcNow`,
  `DateTimeOffset.UtcNow`/`Now`, paired Stopwatch timestamp measurements, Task delays/
  timed waits and explicit `new PeriodicTimer(TimeSpan)` or
  `new CancellationTokenSource(TimeSpan)` constructors. The consumer chooses the
  application's clock; the library requires a TimeProvider local, parameter or
  instance member. `DateTime.Now`/`Today`, ambiguous timestamp uses and unsupported
  argument shapes are withheld. Waits need
  an explicit TimeSpan and optional explicit CancellationToken; named/default shapes
  outside the proof are withheld. Either end of a Stopwatch measurement yields one fix
  that rewrites the timestamp and every elapsed-time use together, so selecting one end
  is enough. Timer creations with an object initializer are withheld.
- `Fix.For(property).UseInitOnly().SafeWhen(symbol => ...)` converts an unrestricted
  automatic setter to init, proves symbol correspondence and compilation, and asks the
  consumer to approve its construction/serialization contract. Implemented or restricted
  setters are withheld.
- `Fix.For(creation).AddInitializer(member, expression).SafeWhen(value => ...)` adds a
  previously unassigned simple member, proves unchanged constructed type, enclosing
  bindings, compiler-supplied arguments such as `CallerArgumentExpression` and binding to
  that member, and asks the consumer to prove the new value. Existing assignments remain.

Adding `static` to a sealed class replaces the written `sealed` token; `ModifierChange`
reports both the removed and added tokens. Rename permits a destination name in an
unrelated scope, but rebinds references spelled like either name to reject capture or
shadowing. Independent argument additions validate their own changed defaults in an
atomic batch; caller-info changes around other edits remain unsafe. Evaluation-count
checks with no registered inputs succeed only when both bounded evaluation traces have
no child evaluations, so empty collections need no special-case bypass.

`Fix.For(statementNode).UseAssertAll().Propose()` converts a synchronous `foreach (var ...)`
containing one xUnit assertion to `Assert.All`. The collection must implement
`IEnumerable<T>` of the exact loop element type; the inner assertion must keep its binding.
Await/ref expressions, pattern-only enumerators and a public `GetEnumerator` that is not
the interface's (outside arrays and the common `System.Collections.Generic` collections)
are withheld. Choosing this builder
explicitly accepts xUnit's aggregated failure reporting instead of stopping at the first
failing item. It does not decide which test loops should use collection assertions.
