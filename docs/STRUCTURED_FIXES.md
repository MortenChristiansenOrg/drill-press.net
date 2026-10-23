# Structured edits with contextual proofs

All operations start from `Fix.For(...)`, use original snapshots, reject compiler
errors/generated/inactive source, and run in every affected loaded context.
Unknown evidence retains the finding without a fix. The engine validates combined
edits and exports their proven union atomically. Finish with `Propose()`, where
the library owns the proof, or `SafeWhen(...)`; `AddArgumentOfType` instead takes
the proof and returns a proposal directly.

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

## Argument addition

```csharp
Fix.For(call).AddArgument("comparisonType", "StringComparison.Ordinal")
    .ExpectOverloadChange(from: startsWith, to: startsWithComparison)
    .SafeWhen(change => change.AddedValue.RefersTo(ordinal));

Fix.For(call).AddArgument("cancellationToken", "TestContext.Current.CancellationToken")
    .SafeWhen(change => change.AddedValue.RefersTo(currentToken));
```

`AddArgument(parameter, expression)` mirrors argument removal. Without an expected
overload change it supplies an omitted optional parameter of the same overload, such
as a defaulted `CancellationToken`; the call must still bind to the same method.
With `ExpectOverloadChange(from, to)` or `ExpectTransition(...)` the call must move to
exactly that overload, keeping its constructed type arguments. The value is
positional when it follows the written arguments in parameter order and named
otherwise. The library checks that the new argument binds to the named parameter,
retained arguments and the receiver keep their order, parameters, conversions and
bindings, enclosing expressions bind as before, and no other compiler-supplied
value changes; expanded `params` calls are withheld. `ArgumentAdditionChange`
exposes both calls, the bound `AddedValue`, `Rewrite`, `Same(before, after)` and
`Expected` (null without an overload change).

What the added value means is always the rule's proof. Some additions change
behavior on purpose, such as passing `CultureInfo.InvariantCulture` where the
current culture applied before: the `SafeWhen` proof then states that the new
behavior is the intended one, not that it is equivalent. There is no separate
behavior-change marker; a `+` means the rule's proof accepted the edit.

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

## Renaming

```csharp
rules.Rule("LOCAL-NAMES", "Name locals in camelCase.")
    .For(Code.Declarations.Where(declaration => declaration.Symbol is ILocalSymbol && char.IsUpper(declaration.Name[0])))
    .Forbid(fix: local => Fix.For(local).Rename(char.ToLowerInvariant(local.Name[0]) + local.Name[1..]).Propose());
```

`Rename(newName)` replaces the declaring identifier and every reference to the symbol,
including named arguments, `nameof` and the `name` of documentation `param` and
`paramref` elements. A keyword name is written with `@`. It renames locals, local
functions, parameters, fields, properties and ordinary methods; it withholds virtual,
abstract, overriding, extern, partial and interface-implementing members, `Main`, lambda
parameters (minimal APIs bind them by name) and record parameters.

`Propose()` owns the proof when only the containing member or type can observe the name:
locals, local functions and their parameters, parameters of private methods, and private
members. It searches the member, or the outermost containing type when all its parts are
in one file, and every loaded source otherwise. Wider renames need
`SafeWhen(change => ...)` over `RenameChange`, where you state that reflection,
serialization, model binding and unloaded assemblies do not depend on the old name.

Either way, the fix is withheld when:

- a declaration or reference is in generated or non-editable source;
- a reference is in another project than the declaration, because each compilation is
  validated against its project references as they were;
- rebinding shows that an existing name would be captured or shadowed;
- a string literal of any kind (regular, verbatim, raw, UTF-8 or interpolated text) in the
  searched source equals the old name, because it may name the symbol through reflection
  or an attribute such as `NotNullIfNotNull`;
- inactive `#if` code in the searched source names the old or new name, because it is
  neither renamed nor rebound.

In every affected compilation, each renamed reference must bind to the renamed symbol,
and every other simple or generic name spelled like either the old or new name, with its
enclosing expressions and including those in the other files of a partial type, must keep
its symbol, type and conversion. An unrelated destination name
in another scope does not prevent a rename. `nameof` results follow
the new name, as a rename intends, but a compiler-supplied argument such as
`CallerArgumentExpression` that would change withholds the fix.

## Reusable structural and framework edits

- `Fix.For(expression).UseCollectionExpression().Propose()` converts supported standard
  collections/arrays with a written target type, preserving the concrete type and each
  element's evaluation count and order. Nonempty dictionary initializers, capacity
  constructors and interface targets changing concrete type are withheld.
- `Fix.For(namespaceNode).UseFileScopedNamespace().Propose()` converts a file's single
  block namespace and removes one four-space indentation level. Nested namespaces,
  directives, multiline text and ambiguous layouts are withheld, and so is a conversion
  that would change a string's value or a compiler-supplied argument such as a caller
  line number. A consumer must exclude
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
  instance member. Either end of a Stopwatch measurement yields one fix that rewrites the
  timestamp and every elapsed-time use together. Local DateTime kinds, ambiguous timestamp
  uses, timer creations with an object initializer and unsupported argument shapes are
  withheld.
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
