# Contextual expression fixes

`DrillPress.Fix.For(candidate)` creates an immutable plan. `ReplaceWith`
accepts Roslyn expression syntax, preserves exterior trivia, and parenthesizes
compound replacements. Interior comments/directives, generated/noneditable
source, `nameof`, expression trees and unresolved compilation errors withhold
fixes. Findings remain reportable.

```csharp
Fix.For(reference.Source, reference.Syntax)
    .ReplaceWith(SyntaxFactory.LiteralExpression(
        SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal("")))
    .Propose(change => IsFrameworkStringEmpty(change.BeforeModel, change.Before)
        ? ProofResult.Proven : ProofResult.Unknown);
```

The required consumer proof establishes the transformation's behavior. Compiler
success alone is insufficient. Default gates compare expression and converted
types, conversions, enclosing overload/operator bindings and compiler-supplied
arguments (including caller argument expressions and caller line numbers).
`Require` adds checks and cannot bypass these gates. Every proof runs against the
actual edited compilation in each loaded membership, including linked contexts
that did not report a finding.

For retained operands, use `Fix.Input(source, expression)`, embed its annotated
`Syntax` in the replacement and register it with `MapInputs`. Mappings retain
occurrence identity and compare binding/conversion after reparsing. Add
`RewriteChecks.SameEvaluationCounts` and `SameEvaluationSequence` when the
transformation requires once-only source-order evaluation. The sequence proof
supports straight-line expressions and compares remaining operations too;
conditional/deferred/short-circuit forms return unknown. The changed root
operation itself is the consumer proof's responsibility. Registration is explicit;
it does not infer logical parameter roles from position.

`SameReceiverNullBehavior` checks instance invocation/member receiver changes.
A nonnullable annotation is not proof that an object cannot be null at runtime.
An exact method-pair equivalence, exceptions, static initialization, allocation,
identity, overloaded operators and concurrency remain transformation-specific
proof obligations. None is inferred from successful compilation.

`RewriteContext.Map(source, originalNode)` maps unchanged nodes and replacement
roots through the complete batch, with a cached rewritten semantic model.
Replaced descendants need explicit input mappings; ambiguous correspondence
returns null. `TreeFor` uses compilation membership, not a filename lookup.

The engine first rejects conflicting batches, then groups remaining proposals
that share affected compilation contexts. It validates their complete union
against every proposal's proof, and exports that proved union atomically. A
failure withholds the group while keeping findings and independent contexts.
This prevents two individually safe replacements from selecting a different
overload together. Legacy `FixProposal` validators without combined validation
can be used alone but cannot authorize a union. `SourceChanges.Propose` supplies
combined validation for custom atomic edits; its proof must inspect the provided
whole-batch `RewriteContext` rather than independently rewriting the original.

## Readable proofs and construction

All rule-authoring types use `using DrillPress;`. A proof can inspect semantic
wrappers bound to the actual before/after compilations:

```csharp
var empty = CodeType.Of<string>().Member("Empty");
Fix.For(reference).ReplaceWith(Code.Literal(""))
    .SafeWhen(change => change.Expressions is { } pair
        && pair.Before.RefersTo(empty) && pair.After.IsConstant(""));
```

`SafeWhen` accepts a Boolean or `ProofResult`; false means unknown and withholds
the fix. Source-less references safely produce no proposal. `Expressions` is
absent for non-expression edits. `RetainedExpressions` exposes each original
input and every mapped replacement occurrence, including zero or repeated uses.
The raw syntax/model properties remain available. Rewritten sources are read-only
and always use the rewritten compilation. `RefersTo` unwraps only parentheses and
implicit non-user conversions; this identifies a member without approving loss
of its conversion or evaluation.

`Code.Literal` preserves primitive/enum C# types. `Code.Expression("{0} == {1}",
leftInput, rightInput)` and `Code.Equal(leftInput, rightInput)` preserve syntax-bound
holes and occurrence mappings. Input parentheses are omitted when reparsing
preserves the expression structure, including precedence and associativity.
`ReplaceWith(template)` registers
all supplied inputs, including unused ones. `Keeping` registers inputs used in
hand-built syntax. Add `MustPreserve(Behavior.EvaluationCounts |
Behavior.EvaluationSequence)` where applicable. Repeated or missing holes fail
the count proof. `ReplaceWithEquality(left, right, absorbNegation: true)` can
absorb an enclosing built-in Boolean `!` into `!=`; explicit operand selection
and a transformation-specific behavior proof are still required. Construction
never proves that a method call and an operator are equivalent.

The evaluation-sequence proof ignores constant reads and treats built-in Boolean
negation as a transparent wrapper around the replaced root. Other nested calls
remain observable, and reordered operands still fail the proof. Receiver-null
checks also look through built-in negation: absorbing `!receiver.Equals(...)`
does not establish that `receiver` is non-null.

`CodeExpression.Facts` exposes expression-tree and actual `nameof` context,
interior comments, directives and disabled text. These explain default builder
eligibility; they are not opt-outs. The low-level `SourceChanges` API validates
atomic edits and their contextual proof, but does not inherit every high-level
builder's source-shape and trivia restrictions.

| Builder | Library-owned checks | Additional obligations |
| --- | --- | --- |
| Expression replacement | Editable source, safe trivia/context, compiler success, enclosing and retained-input bindings/conversions, compiler-supplied arguments | Evaluation count/order, receiver behavior, transformation equivalence |
| Argument removal | Explicit removable argument, safe source/context, exact constructed transition, retained values/conversions/order and enclosing bindings | Removed value, lost evaluation, overload behavior; explicit approval for changed synthesized arguments |
| Modifier removal | One editable modifier, safe trivia, declared symbols and compiler-supplied arguments | Accessibility/identity/contract and behavior; no-argument `Propose()` supplies these only for redundant top-level `internal` |
| Braces | Restricted if/else statement, safe boundaries, identical statement/branch ownership and bound expressions | Library owns the restricted structural proof |
| Extraction | Editable partial owner, matching group/value/shape/capture and call bindings, enclosing bindings, compiler-supplied arguments | Templates require formatting/culture, moved-call and evaluation proof; constants support no-argument `Propose()` |

All checks run in every affected compilation, including linked contexts without
findings, against the complete combined batch. `MustPreserve` selects bounded
checks (`Bindings`, `EvaluationCounts`, `EvaluationSequence`,
`NullReceiverBehavior`, or declaration `Accessibility`, `Identity`, `Contract`).
There is no general “all semantics” guarantee.

## Framework overload transitions

`CodeType.Framework("System.Linq.Enumerable")` requires a framework
runtime/reference assembly identity and rejects source lookalikes. `Named`
remains a name-only query. Descriptor transitions preserve the call's constructed
generic arguments and normalize reduced extension/static spelling:

```csharp
Fix.For(argument).Remove()
    .ExpectOverloadChange(from: distinctWithComparer, to: distinct)
    .RequireRemovedValue(change => change.RemovedValue!.RefersTo(ordinal))
    .RequireRemovedEvaluation(ApprovesOrdinalEvaluation)
    .SafeWhen(ProvesDistinctStringEquivalence);
```

The target must be unique. Identical parameter names, ref kinds and bound types
infer a map; otherwise use `MethodTransition` with an explicit map. The actual
edited overload is still checked. Matching `StringComparer.Ordinal` alone does
not authorize removing its getter, conversion or initialization behavior.
