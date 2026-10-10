# Fixes

A fix factory receives the violating candidate and returns `FixProposal?`. Null keeps
the finding without a fix. Every chain starts at `Fix.For(candidate)` (or
`Fix.Extract(group)`) and ends in `Propose()` or `SafeWhen(proof)`.

## Builders

| `Fix.For(...)` of | Edit | Finish |
| --- | --- | --- |
| `CodeExpression`, `MemberReference`, `CodeObjectCreation`, `CodeInvocation` | `ReplaceWithLiteral(value)`, `ReplaceWith("{0} == {1}", a, b)`, `ReplaceWithEquality(a, b, absorbNegation: true)`, `ReplaceWith(syntax)` | `SafeWhen` |
| `CodeInvocation` / `CodeArgument` | `RemoveArgument("name")` / `Remove()` | `SafeWhen` |
| any `ICodeDeclaration` | `RemoveModifier(Modifier.X)` | `Propose()` for accessibility that stays the same, else `SafeWhen` |
| `CodeBranch` | `AddBraces()` | `Propose()` |
| `CodeComment` | `Remove()`, `Remove(preserveLines: true)` | `Propose()` |
| `CodeVariableDeclaration` | `UseVar()` | `Propose()` |
| `ExpressionGroup` via `Fix.Extract(group)` | `ToConstant(name)`, `ToMethod(name, parameterName)` | `Propose()` (constants), `SafeWhen` (templates) |

Every builder already withholds the fix for generated, inactive or non-editable source,
`nameof` and expression trees, interior comments or directives, compiler errors after
the edit, and changed bindings of the surrounding code, kept operands and
compiler-supplied arguments (caller info). It validates the whole batch in every
affected compilation, including linked files and other target frameworks.

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
- `MustPreserve(ExpressionBehavior.EvaluationCounts)` requires each kept operand to run
  exactly once; `EvaluationOrder` keeps straight-line order; `NullReceiverBehavior`
  requires a receiver whose null check would move to be intrinsically non-null.
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

## Modifiers, braces, comments and var

Comment removal deletes standalone lines by default. Use `Remove(preserveLines: true)`
to keep every line break, including those inside multiline comments, when caller
line numbers matter. Parsing and compiler-supplied arguments must still be unchanged
in every affected loaded context; caller argument text can still prevent a fix.

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
Custom edits do not get the builders' trivia and binding checks.

## Testing fixes

Assert `result.Output` (validated fixes show `+` before the line) and the complete
`result.FixedText(path)`. Add a case where the proof fails and assert the finding has
no `+` and `FixedText` returns the original text.
