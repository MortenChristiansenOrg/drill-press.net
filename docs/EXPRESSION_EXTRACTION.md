# Selected expression grouping and extraction

Use `ExpressionGroups` with an ordinary `CodeQuery<CodeExpression>` selecting the
sites your rule owns. The SDK groups only those inputs; it does not search for
additional occurrences or impose route, naming, or request-API policy.

```csharp
var constants = ExpressionGroups.Constants(selectedExpressions);
var templates = ExpressionGroups.OneHoleTemplates(selectedExpressions,
    new OneHoleTemplateOptions(
        TemplateShapes.Interpolation | TemplateShapes.Concatenation,
        capture => capture.TypeIs(CodeType.Of<string>()),
        new ApiSet(configuredEncoder)));

rules.For(constants).Forbid("CONSTANT", "Extract the repeated value.",
    fix: group => Fix.Extract(group)
        .ToConstant("SharedValue")
        .Propose(ProveConstantExtraction));

rules.For(templates).Forbid("TEMPLATE", "Extract the repeated template.",
    fix: group => Fix.Extract(group)
        .ToMethod("FormatValue", parameterName: "value")
        .Propose(ProveHelperExtraction));
```

The named proof functions return `ProofResult` from `ExtractionEvidence`; only
`Proven` permits a fix. Grouping is candidate discovery, not behavioral proof.

## Candidate equivalence

Constants group by compiler type and value, distinguishing enum types, present
null values and floating-point bit patterns. Literal spelling is immaterial.
Each occurrence retains its original type, converted type and conversion evidence.
An optional `additionalEquivalence` relation can further partition candidates; it
is compared against the first occurrence of each partition.

Templates support explicitly selected ordinary string interpolation and built-in
string concatenation. Exactly one non-ref local or parameter occurrence forms the
hole; a second read of the same variable is a second hole. Fixed values, expression
structure, conversions, format/alignment, and bound call identities must match.
Concatenation and interpolation never become equivalent by algebraic inference.
Static ordinary calls around the hole require an explicit `ApiSet`; allowlisting
asserts neither purity nor behavioral equivalence. Properties, hidden receivers,
extra dependencies, dynamic binding and unsupported syntax produce no group.
Capture predicates and a finite syntax-node budget bound discovery.

Groups combine partial declarations of the same source type in one evaluated
context. Nested types and separate contexts stay separate. Inputs are deduplicated
and ordered by source path and span; query evaluation is cached and cancellable.
`minimumOccurrences` defaults to two and can be one for explicit single-site work.

## Extraction behavior

`ToConstant` creates a private typed constant. With reuse enabled (the default),
it prefers an equivalent constant of the requested name, otherwise requires exactly
one equivalent constant in the owner. It never substitutes a readonly field or
property. References use the fully qualified containing type and are rebound, so a
local with the same name cannot capture the replacement.

`ToMethod` creates a private static string helper with one explicitly typed value
parameter. It moves the supported expression body and maps each occurrence's
capture to its own argument. Existing helpers are not reused. Conversions must
remain exact; for example, moving a boxed integer concatenation behind an `int`
parameter is conservatively withheld, while string concatenation and ordinary
integer interpolation are supported. Method type parameters, non-denotable types,
ref-like types and uncertain bindings are unsupported.

Names must be unescaped C# identifiers. Collisions refuse by default; opt into
`ExtractionNameCollision.AddNumericSuffix` for deterministic allocation. Allocation
considers all partial and inherited members. `.InPart(part)` explicitly selects an
editable ordinary declaration of the same type/context; otherwise the group's
stable owner declaration is used. The destination must exist in every affected
loaded context. Insertions follow local indentation and newline conventions.

The entire insertion and every selected replacement form one atomic proposal.
Unselected expressions remain untouched. Overlap, ambiguous comments/directives,
generated or inactive source, expression trees, `nameof`, checked contexts, or
compiler errors withhold the fix. A null literal can form a constant group while
extraction is withheld when a typed field would change its conversion semantics.

Every affected loaded context validates the complete final batch, including
contexts without a finding. Validation checks the member's type/value or template,
retained captures and enclosing bindings, synthesized caller arguments, and other
expression bindings throughout the compilation that adding a member could affect.
Conflicting member insertions are withheld together. An unavailable context cannot
be proved; no claim is made about unloaded downstream projects.

The required consumer proof receives original/rewritten owners, the actual member,
whether it was reused, per-occurrence rewrite evidence and the whole rewrite
context. It owns domain equivalence, effects of configured calls, evaluation timing,
culture/formatting assumptions and allocation observability. Structural matching
never replaces that proof. Unsupported cases retain their diagnostics without fixes.

## Member captures and complete owner coverage

One-hole templates support locals, parameters, non-indexer properties and fields.
The capture remains evaluated once at the original call site. For example,
`$"/api/{Encode(item.ExternalId)}"` becomes `CreateUrl(item.ExternalId)`;
`Encode(externalId)` remains inside the helper. An optional
`OneHoleTemplateOptions(..., allowingCalls: call => ...)` predicate admits static
wrappers such as class-local/base-class encoders. `ISymbol.IsDeclaredInOrAbove`
can match the owner/base hierarchy. The moved call must still bind to the same
method, and a consumer proof must approve its effects and ordering.

```csharp
Fix.Extract(group)
    .OnlyWhenGroupCoversAll(allRequestUrlValues)
    .ToMethod("CreateUrl", ParameterName.FromCapture)
    .SafeWhen(ProvesUrlTemplate);
```

Coverage includes every selected value across ordinary partial parts in each
validated context, excludes nested types, and must include already-compliant
`_url` or `CreateUrl(...)` uses in the supplied query. Filtering that query to
violations weakens the intended policy. A mismatching or uncovered use withholds
the whole extraction. Parameter names use the deterministic representative
capture (`ExternalId` → `externalId`); an explicit string name overrides it.

Constant groups support `Fix.Extract(group).ToConstant("_url").Propose()` under
the library's restricted constant proof. Templates still require `SafeWhen` or
an explicit `Propose` proof. Attaching the same extraction to every occurrence
produces one deduplicated atomic edit batch.
