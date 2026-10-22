# Structured edits with contextual proofs

All operations start from `Fix.For(...)`, use original snapshots, reject compiler
errors/generated/inactive source, and run in every affected loaded context.
Unknown evidence retains the finding without a fix. The engine validates combined
edits and exports their proven union atomically. Every chain ends with
`Propose()`, where the library owns the proof, or `SafeWhen(...)`.

## Exact argument removal

```csharp
Fix.For(call).RemoveArgument("option")
    .ExpectTransition(new MethodTransition(ResolveExactPair,
        new Dictionary<string, string> { ["source"] = "source" }))
    .RequireRemovedValue(MatchesConfiguredDefault)
    .RequireRemovedEvaluation(ApprovedLossOfEvaluation)
    .SafeWhen(ProveOverloadEquivalence);
```

`Fix.For(argument).Remove()` starts the same plan from a selected `CodeArgument`.
`ExpectOverloadChange(from, to)` declares the transition with descriptors;
`ExpectTransition` accepts a `MethodTransition` whose `ResolveExactPair` returns
fully constructed `IMethodSymbol` values in each original compilation. Actual
overload resolution must choose that exact destination, including assembly,
declaring type, construction and signature. Generic arguments must remain equal.
The explicit parameter map includes every retained parameter, including an
extension receiver. Remaining source arguments keep their order, count, binding,
parameter types/ref kinds and conversions. Ordinary/static/reduced extension
syntax and named argument order are preserved; enclosing bindings cannot be
waived by the pair proof.

The value and evaluation-loss proofs are separate and mandatory. Their
`ArgumentRemovalChange` includes the removed bound operation (getter, declaring
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

## Braces for embedded statements

`Fix.For(branch).AddBraces().Propose()` applies a built-in restricted structural
proof to a `CodeBranch`: an if or else branch (`statement.Then`, `statement.Else`,
`Code.IfStatements.Branches()`) or any other embedded statement from
`Code.EmbeddedStatements`, the bodies of `for`, `foreach`, `while`, `do`, `using`,
`lock` and `fixed` statements. `WithoutBraces()` skips blocks, else-if
continuations and stacked `using (a) using (b)` or `fixed` statements. The
statement must not already be a block. The two boundary edits retain one exact original statement,
including a complete `else if` chain. They preserve original branch ownership,
bound expression symbols/conversions, and caller-supplied argument constants.
Labels, goto, yield, local functions, directives and ambiguous header-to-statement
comments are conservatively unsupported.

The binding check is proportional to the edit. Binding depends on the compilation's
declarations and on the body being bound, so an edit strictly inside a member body,
which is still that member's body afterwards, cannot change how anything outside the
body binds. `RewriteChecks.SameSourceBindings` therefore rebinds only the edited
bodies; edits in initializers, attributes, signatures or top-level statements rebind
the whole compilation. Within a combined batch, expressions edited by another
proposal are left to that proposal's own proof, so an unrelated replacement in the
same method does not withhold the braces. Namespaces compare by their qualified
name: every `namespace Company.Import` declaration merges into one symbol.

Layout reads local indentation and the file newline convention. The closing brace
follows any trailing line comment on a fresh line; existing multiline token text
and unrelated members are untouched. Independent branch edits can coexist. Nested
plans that cannot retain exact original statement correspondence are withheld.
Adding braces is not a general-purpose control-flow equivalence proof.

## Declaration modifiers

### Adding a modifier

```csharp
rules.Rule("SEALED", "Seal classes that nothing derives from.")
    .For(Code.TypeDeclarations.Where(type => type.Symbol is { TypeKind: TypeKind.Class, IsSealed: false, IsAbstract: false, IsStatic: false }))
    .Forbid(fix: type => Fix.For(type).AddModifier(Modifier.Sealed).Propose());
```

`AddModifier(modifier)` inserts one token in the conventional order (accessibility,
`static`, `extern`, `new`, `virtual`, `abstract`, `sealed`, `override`, `readonly`,
`unsafe`, `required`, `volatile`, `async`, with `partial` last), after attributes and
documentation. `Propose()` owns the proof for classes:

- `sealed`: no type in any loaded project derives from the class, and it is not
  abstract or static.
- `static`: every member is static, no constructor is declared, the class derives
  from `object` and implements no interface, nothing derives from it, and every
  loaded use of its name accesses a member, `nameof` or `using static`; `new A()`,
  `typeof(A)`, a variable, parameter or type argument withholds the fix.

Both proofs assume the loaded solution contains every assembly that derives from,
instantiates or reflects over the type, including mocking proxies and dependency
injection. An obstacle found before editing is reported as the withheld reason.
Other modifiers need `SafeWhen(change => ...)` over `ModifierChange`, whose
`Added`/`AddedModifier` describe the new token; `MustPreserve` adds the same
declaration checks as for removal.

### Removing a modifier

```csharp
rules.Rule("INTERNAL", "Omit the redundant internal modifier.")
    .For(Code.TypeDeclarations.TopLevel().WithExplicitModifier(Modifier.Internal))
    .Forbid(fix: type => Fix.For(type).RemoveModifier(Modifier.Internal).Propose());
```

`Fix.For(declaration)` accepts type parts and definitions, methods, fields,
properties, parameters and declared symbols. `Propose()` owns the proof for an
accessibility token (`public`, `private`, `protected`, `internal`) when every
affected symbol keeps its declared accessibility, containing accessibility,
identity and contract, such as an explicit `private` on a member or `internal` on
a top-level type. Removing `protected` from `protected internal` changes the
accessibility and is withheld. Other modifiers need a behavior proof:

```csharp
Fix.For(method).RemoveModifier(Modifier.Static)
    .MustPreserve(DeclarationBehavior.Identity | DeclarationBehavior.Accessibility)
    .SafeWhen(ProveStaticRemoval);
```

One syntax adapter supports types (including records/enums/delegates), methods and
constructors/operators, properties/indexers/events, fields, accessors, parameters
and local functions. Only the selected actual modifier and adjacent horizontal
whitespace are removed. Comments/newlines/attributes and adjacent tokens survive;
directives, missing/duplicate tokens and recovery syntax are refused.

`ModifierChange.Symbols` maps every affected declarator. Partial symbols use
complete compiler semantics, including generated parts, while only explicitly
selected ordinary syntax is edited. Identity includes contextual declaration
correspondence and partial-method pairing; raw symbol equality or a documentation
ID alone is insufficient. Declared accessibility is distinct from containing
accessibility. `DeclarationBehavior.Contract` also checks static/virtual/readonly/async
and related compiler flags, but none of these invariants replaces the required
behavior proof for an arbitrary modifier. The SDK has no preference for implicit
accessibility.
