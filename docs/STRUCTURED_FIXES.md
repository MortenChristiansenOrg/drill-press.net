# Structured edits with contextual proofs

All operations start from `Fix.For(...)`, use original snapshots, reject compiler
errors/generated/inactive source, and run in every affected loaded context.
Unknown evidence retains the finding without a fix. The engine validates combined
edits and exports their proven union atomically.

## Exact argument removal

```csharp
Fix.For(call).RemoveArgument("option")
    .RequireTransition(new MethodTransition(ResolveExactPair,
        new Dictionary<string, string> { ["source"] = "source" }))
    .RequireRemovedValue(MatchesConfiguredDefault)
    .RequireRemovedEvaluation(ApprovedLossOfEvaluation)
    .Propose(ProveOverloadEquivalence);
```

`ResolveExactPair` returns fully constructed `IMethodSymbol` values in each original
compilation. Actual overload resolution must choose that exact destination,
including assembly, declaring type, construction and signature. Generic arguments
must remain equal. The explicit parameter map includes every retained parameter,
including an extension receiver. Remaining source arguments keep their order,
count, binding, parameter types/ref kinds and conversions. Ordinary/static/reduced
extension syntax and named argument order are preserved; enclosing bindings cannot
be waived by the pair proof.

The value and evaluation-loss proofs are separate and mandatory. Their
`ArgumentRemovalEvidence` includes the removed bound operation (getter, declaring
type/assembly, conversion), both actual calls, configured pair and whole rewritten
context. A known static property is not generally pure. Initialization, exceptions,
side effects and observable conversions must be justified locally for this pair.
The sample `Distinct<string>` policy demonstrates an approved framework contract;
there is no LINQ exception in shared infrastructure.

Omitted values, receivers and params parameters are not removable in this version.
Argument-list comments/directives whose ownership is ambiguous withhold the fix;
exterior call trivia is retained. Changed synthesized arguments require a separate
`RequireSynthesizedArguments` proof at this exact call. Other calls' caller-info
values remain protected. Unsupported retained calls whose correspondence cannot
be established conservatively withhold the edit.

## Embedded statement blocks

`Fix.For(statement).WrapInBlock().Propose()` applies a built-in restricted structural
proof. The selected statement must occupy an actual `if` or `else` slot and cannot
already be a block. The two boundary edits retain one exact original statement,
including a complete `else if` chain. They preserve original branch ownership,
bound expression symbols/conversions, and caller-supplied argument constants.
Labels, goto, yield, local functions, directives and ambiguous header-to-statement
comments are conservatively unsupported.

Layout reads local indentation and the file newline convention. The closing brace
follows any trailing line comment on a fresh line; existing multiline token text
and unrelated members are untouched. Independent branch edits can coexist. Nested
plans that cannot retain exact original statement correspondence are withheld.
Adding braces is not a general-purpose control-flow equivalence proof.

## Declaration modifiers

```csharp
Fix.For(declaration).RemoveModifier(SyntaxKind.InternalKeyword)
    .Require(DeclarationChecks.SameIdentity)
    .Require(DeclarationChecks.SameDeclaredAccessibility)
    .Require(DeclarationChecks.SameContract)
    .Propose(ProveSelectedAccessibilityRemoval);
```

One syntax adapter supports types (including records/enums/delegates), methods and
constructors/operators, properties/indexers/events, fields, accessors and local
functions. Only the selected actual modifier and adjacent horizontal whitespace
are removed. Comments/newlines/attributes and adjacent tokens survive; directives,
missing/duplicate tokens and recovery syntax are refused.

`DeclarationRewrite.Symbols` maps every affected declarator. Partial symbols use
complete compiler semantics, including generated parts, while only explicitly
selected ordinary syntax is edited. Identity includes contextual declaration
correspondence and partial-method pairing; raw symbol equality or a documentation
ID alone is insufficient. Declared accessibility is distinct from containing
accessibility. `SameContract` also checks static/virtual/readonly/async and related
compiler flags, but none of these invariants replaces the required behavior proof
for an arbitrary modifier. The SDK has no preference for implicit accessibility.
