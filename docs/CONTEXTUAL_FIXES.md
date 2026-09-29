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
