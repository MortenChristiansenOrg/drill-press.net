# Contextual expression fixes

`Fix.For(expression)` starts an immutable plan for an expression, member
reference, object creation or call. Choose the replacement, optionally add
bounded behavior checks, and end with `SafeWhen(proof)`:

```csharp
var empty = CodeType.Of<string>().Member("Empty");

rules.Rule("EMPTY", "Use \"\" instead of string.Empty.")
    .For(empty.References.OutsideNameOf())
    .Forbid(fix: reference => Fix.For(reference)
        .ReplaceWithLiteral("")
        .SafeWhen(change => change.Before.RefersTo(empty) && change.After.Is("")));
```

| Replacement | Use it for |
| --- | --- |
| `ReplaceWithLiteral(value)` | A typed string, character, Boolean, number, enum value or null literal. Numeric and enum types are preserved. |
| `ReplaceWith("{0} == {1}", left, right)` | A C# expression template whose holes embed original sub-expressions unchanged. |
| `ReplaceWithEquality(left, right, absorbNegation: true)` | `left == right`; an enclosing built-in `!` becomes `left != right`. |
| `ReplaceWith(syntax)` | Hand-built Roslyn syntax; kept operands are not tracked. |

The proof receives an `ExpressionChange`. `Before` and `After` are
`CodeExpression` views bound to the original and fully rewritten compilations,
with their type, constant and symbol helpers. `Kept` lists each original operand
with every use in the replacement, including zero or repeated uses. `Rewrite`
exposes the raw syntax, semantic models and complete edit batch. A false proof
withholds the fix and keeps the finding. Rewritten sources are read-only and
always use the rewritten compilation. `RefersTo` unwraps only parentheses and
implicit non-user conversions; this identifies a member without approving loss
of its conversion or evaluation.

The proof establishes the transformation's behavior. Compiler success alone is
insufficient. Built-in gates withhold fixes in generated or non-editable source,
for interior comments, directives or disabled text, inside `nameof` and
expression trees, and when any affected compilation has errors. They compare
expression and converted types, conversions, enclosing overload/operator
bindings, kept operands' bindings and compiler-supplied arguments (including
caller argument expressions and caller line numbers). Every proof runs against
the actual edited compilation in each loaded membership, including linked
contexts that did not report a finding.

Template holes and the whole replacement are parenthesized only where the
destination would otherwise parse differently, so `!string.Equals(a, b)` becomes
`a != b` while `F() * 2` with `F()` replaced by `a + b` becomes `(a + b) * 2`.

## Bounded behavior checks

`MustPreserve` adds library-owned checks of kept operands:

| `ExpressionBehavior` | Meaning |
| --- | --- |
| `EvaluationCounts` | Each kept operand is evaluated exactly once; repeated or missing holes fail. |
| `EvaluationOrder` | Kept operands and remaining observable evaluations keep their straight-line order and count. Conditional, deferred and short-circuit forms are unknown and withhold the fix. |
| `NullReceiverBehavior` | An instance receiver whose null check would move or disappear must be intrinsically non-null. A nonnullable annotation is not proof. |

The evaluation-order check ignores constant reads and treats built-in Boolean
negation as a transparent wrapper around the replaced root. Other nested calls
remain observable, and reordered operands still fail. Receiver-null checks also
look through built-in negation: absorbing `!receiver.Equals(...)` does not
establish that `receiver` is non-null. An exact method-pair equivalence,
exceptions, static initialization, allocation, identity, overloaded operators
and concurrency remain transformation-specific proof obligations. There is no
general "all semantics" guarantee, and construction never proves that a method
call and an operator are equivalent.

```csharp
rules.Rule("EQUALS", "Use == for ordinal string equality.")
    .For(Code.Calls.To(CodeType.Of<string>().Member("Equals")))
    .Forbid(fix: call => Fix.For(call)
        .ReplaceWithEquality(call.Argument("a")!.Value!, call.Argument("b")!.Value!,
            absorbNegation: true)
        .MustPreserve(ExpressionBehavior.EvaluationCounts | ExpressionBehavior.EvaluationOrder)
        .SafeWhen(change => change.Kept.All(operand => operand.Before.TypeIs<string>())));
```

`CodeExpression.Facts` exposes expression-tree and actual `nameof` context,
interior comments, directives and disabled text. These explain default builder
eligibility; they are not opt-outs.

## Combined validation

`RewriteContext.Map(source, originalNode)` maps unchanged nodes and replacement
roots through the complete batch, with a cached rewritten semantic model.
Replaced descendants need explicit input mappings; ambiguous correspondence
returns null. `TreeFor` uses compilation membership, not a filename lookup.

The engine first rejects conflicting batches, then groups remaining proposals
that share affected compilation contexts. It validates their complete union
against every proposal's proof, and exports that proved union atomically. A
failure withholds the group while keeping findings and independent contexts.
This prevents two individually safe replacements from selecting a different
overload together. `SourceChanges.Propose` supplies combined validation for
custom atomic edits; its proof must inspect the provided whole-batch
`RewriteContext` rather than independently rewriting the original. The
low-level path does not inherit the typed builders' source-shape and trivia
restrictions.

| Builder | Library-owned checks | Additional obligations |
| --- | --- | --- |
| Expression replacement | Editable source, safe trivia/context, compiler success, enclosing and kept-operand bindings/conversions, compiler-supplied arguments | Evaluation count/order, receiver behavior, transformation equivalence |
| Argument removal | Explicit removable argument, safe source/context, exact constructed transition, retained values/conversions/order and enclosing bindings | Removed value, lost evaluation, overload behavior; explicit approval for changed synthesized arguments |
| Modifier removal | One editable modifier, safe trivia, declared symbols and compiler-supplied arguments | Behavior; `Propose()` supplies the proof for accessibility tokens whose removal keeps the declared accessibility, identity and contract |
| Braces | Restricted if/else statement, safe boundaries, identical statement/branch ownership and bound expressions | Library owns the restricted structural proof |
| Comment removal | Unchanged code tokens and compiler-supplied arguments | Library owns the proof |
| `var` conversion | Identical inferred type, nullable annotations, tuple names and enclosing bindings | Library owns the proof |
| Extraction | Editable partial owner, matching group/value/shape/capture and call bindings, enclosing bindings, compiler-supplied arguments | Templates require formatting/culture, moved-call and evaluation proof; constants support `Propose()` |

All checks run in every affected compilation, including linked contexts without
findings, against the complete combined batch.

## Framework overload transitions

`CodeType.Framework("System.Linq.Enumerable")` requires a framework
runtime/reference assembly identity and rejects source lookalikes. `Named`
remains a name-only query. Descriptor transitions preserve the call's constructed
generic arguments and normalize reduced extension/static spelling:

```csharp
Fix.For(argument).Remove()
    .ExpectOverloadChange(from: distinctWithComparer, to: distinct)
    .RequireRemovedValue(change => change.RemovedValue?.RefersTo(ordinal) == true)
    .RequireRemovedEvaluation(ApprovesOrdinalEvaluation)
    .SafeWhen(ProvesDistinctStringEquivalence);
```

The target must be unique. Identical parameter names, ref kinds and bound types
infer a map; otherwise use `ExpectTransition(new MethodTransition(...))` with an
explicit map. The actual edited overload is still checked. Matching
`StringComparer.Ordinal` alone does not authorize removing its getter, conversion
or initialization behavior.
