# Authoring API migration

This preview reshapes the authoring API so that each concept has one spelling.
Every capability of the previous surface remains available; this table maps each
removed or renamed API to its replacement. Rebuild rule bundles after upgrading.

## Rule registration

Every rule now starts with its identity. `Forbid` and `Require` no longer take an
ID or message, and location selectors moved to `ReportAt`.

| Before | After |
| --- | --- |
| `new RuleSet()`, `using RuleSet = DrillPress.RuleSet;` | `new RuleCatalog()`; no alias is needed beside `using Microsoft.CodeAnalysis;` |
| `rules.For(q).Forbid("ID", "Message")` | `rules.Rule("ID", "Message").For(q).Forbid()` |
| `rules.For(q).Require(predicate, "ID", "Message")` | `rules.Rule("ID", "Message").For(q).Require(predicate)` |
| `rules.For(q).Require("ID", "Message", when: predicate)` | `rules.Rule("ID", "Message").For(q).Require(predicate)` |
| `Forbid(..., fixComplexity: level)` | `rules.Rule("ID", "Message", fixComplexity: level)` |
| `Forbid(descriptor)` | `rules.Rule(descriptor).For(q).Forbid()` |
| `Forbid(..., location: x => ...)` / `at: x => ...` | `.ReportAt(x => ...)` before `Forbid`/`Require` |
| `query.At(x => anchor)` and `LocatedCandidate<T>.Value` | report the value directly with `.ReportAt(x => anchor)` |
| `RuleScope<T>` | `RuleClause<T>` from `RuleDefinition.For` |
| `rules.Evaluate(memberReferences)` | `rules.Evaluate(solution)`; member references always have source |

`Forbid` and `Require` return the `RuleDefinition`, so further clauses chain with
`.For(...)`. A rule without any `Forbid` or `Require` clause now fails evaluation.

## Discovery roots

`Code` is the only root. The former static classes are internal.

| Before | After |
| --- | --- |
| `Sources.Files`, `Sources.Projects`, `Sources.FilesIncludingGenerated` | `Code.Files`, `Code.Projects`, `Code.FilesIncludingGenerated` |
| `Sources.Nodes<T>()`, `Sources.Attributes` | `Code.Nodes<T>()`, `Code.Nodes<AttributeSyntax>()` |
| `OperationQueries.Invocations`, `OperationQueries.NullChecks` | `Code.Calls`, `Code.NullChecks` |
| `OperationQueries.Of<T>()`, `OperationQueries.All` | `Code.Operations<T>()`, `Code.Operations<IOperation>()` |
| `OperationQueries.InFiles(files)`, `OperationQueries.InvocationsIn(files)` | `files.Operations<IOperation>()`, `files.Calls()` |
| `SymbolQueries.Declarations`, `SymbolQueries.DeclarationsIn(files)` | `Code.Declarations`, `files.Declarations()` |
| `SymbolQueries.ReferencesTo(symbol)` | `Code.ReferencesTo(symbol)` |
| `ProjectFacts.TestFiles`, `ProjectFacts.ProductionFiles` | `Code.Files.InTestProjects()`, `Code.Files.InNonTestProjects()` |
| `ProjectFacts.WithRole(predicate)` | `Code.Projects.Where(predicate)` |
| `project.ReferencesPackage(id)` (extension) | the same call, now an `AnalysisProject` method |
| `TestDiscovery.TestMethods`, `TestDiscovery.TestClasses` | `Code.TestMethods`, `Code.TestClasses` (now also NUnit and MSTest) |
| `TestDiscovery.Classes(includeAbstract: true)` | `Code.TestClasses.Union(Code.AbstractTestClasses)` |
| `Members.Are(type, name)` | `type.Member(name).References` |
| `bodies.Invocations()`, `body.Invocations()`, `files.Invocations()` | `bodies.Calls()`, `body.Calls()`, `files.Calls()` |
| `NullCheckQueries.In(...)`, `NullCheckQueries.Match(...)` | `Code.NullChecks`, `bodies.NullChecks()` |

## Renamed types and members

| Before | After |
| --- | --- |
| `CodeDeclaration` (semantic type) | `CodeTypeDefinition` |
| `CodeTypedDeclaration` (local, foreach and out variable) | `CodeVariableDeclaration` |
| `calls.Calling(member)` | `calls.To(member)` |
| `calls.ToMethodsMatchingName(pattern)` | `calls.ToMethodsMatching(pattern)` |
| `WhereNameContainsAnyWord(words)` | `WithNameContainingAnyWord(words)`, on every declaration kind |
| `methods.Named(...)`, `WithAttribute(...)`, `WithExplicitModifier(...)` | the same names, now generic over every `ICodeDeclaration` |
| `element.IsDeclaredInProject(name)` | `element.IsInProject(glob)`; `InProject` also accepts globs |
| `call.Argument(name)` returning `IArgumentOperation` | `call.Argument(name)` returning `CodeArgument` |
| `expression.IsConstant(value)`, `expression.IsText(text)` | `expression.Is(value)` |
| `reference.AsExpression()` (nullable) | `reference.Expression` |
| `reference.Source!`, `reference.Syntax!`, `reference.Facts!` | non-nullable `Source`, `Syntax`, `Symbol` and `Facts` |
| `ImplementationView.Owner` | `ImplementationView.Interface` |
| `views.WhereConcrete()` | `views.ConcreteOnly()` |
| `reachability.Declarations(owner)` | `reachability.Definitions(owner)` |
| `ConditionPattern.IsNull()`, `NullTests(name)` | `ConditionPattern.Null` |
| `ConditionPattern.IsEmptyString()` and the other factories | `ConditionPattern.EmptyString`, `NullOrEmptyString`, `NullOrWhiteSpaceString` |
| `ExpressionGroups.Constants(expressions, ...)` | `expressions.ConstantGroups(...)` |
| `ExpressionGroups.OneHoleTemplates(expressions, options, ...)` | `expressions.TemplateGroups(options, ...)` |
| `DuplicateSyntax.In(nodes, minimumTokens)` | `nodes.Duplicates(minimumTokens)` |
| `statement.BranchWithoutBraces` (only when one side of an if/else has braces) | `statement.InconsistentlyBracedBranch`; every unbraced branch is `Code.IfStatements.Branches().WithoutBraces()` |

## Fixes

Fix builders are typed by what they edit, and every chain ends with `Propose()`
or `SafeWhen(...)`. Proofs are Boolean; a false proof withholds the fix.

| Before | After |
| --- | --- |
| `Fix.For(node)` on any `CodeNode<T>` | `Fix.For(node.AsExpression())` for expressions; use typed elements for declarations and branches |
| `Fix.For(source, syntax)` | `SourceChanges.Propose(...)` for hand-built edits |
| `ReplaceWith(Code.Literal(value))` | `ReplaceWithLiteral(value)` |
| `ReplaceWith(Code.Expression("{0} == {1}", Fix.Input(a), Fix.Input(b)))` | `ReplaceWith("{0} == {1}", a, b)` with `CodeExpression` operands |
| `Code.Equal(...)`, `ReplaceWithEquality(Fix.Input(a), Fix.Input(b))` | `ReplaceWithEquality(a, b)` |
| `.MapInputs(...)`, `.Keeping(...)` | pass kept operands to the template overload |
| `.Propose(change => condition ? ProofResult.Proven : ProofResult.Unknown)` | `.SafeWhen(change => condition)` |
| `.Require(RewriteChecks.SameEvaluationCounts)` and similar | `.MustPreserve(ExpressionBehavior.EvaluationCounts)` |
| `Behavior.EvaluationSequence` | `ExpressionBehavior.EvaluationOrder` |
| `Behavior.Accessibility / Identity / Contract` | `DeclarationBehavior.Accessibility / Identity / Contract` |
| `.Require(DeclarationChecks.SameContainingAccessibility)` | `.MustPreserve(DeclarationBehavior.ContainingAccessibility)` |
| `Behavior.Bindings` | always enforced; omit it |
| `change.Expressions!.Before / After` | `change.Before`, `change.After` |
| `change.RetainedExpressions` | `change.Kept` |
| `RemoveModifier(SyntaxKind.InternalKeyword)` | `RemoveModifier(Modifier.Internal)` |
| `RemoveModifier(...).Propose()` (top-level `internal` only) | `Propose()` for any accessibility token that keeps the declared accessibility |
| `WrapInBlock()` | `AddBraces()` |
| `Fix.For(comment).Remove()` | `Fix.For(comment).Remove().Propose()` |
| `Fix.For(local).UseVar()` | `Fix.For(local).UseVar().Propose()` |
| `RequireTransition(transition)` | `ExpectTransition(transition)` |
| `DeclarationRewrite`, `ArgumentRemovalEvidence`, `ExtractionEvidence` | `ModifierChange`, `ArgumentRemovalChange`, `ExtractionChange` |

Replacements are now parenthesized only where the destination requires it, so a
rewrite of `!string.Equals(a, b)` produces `a != b` rather than `(a != b)`.

### Preview.16 extraction evidence

`ExtractionChange.Occurrences` now contains `ExpressionChange` instead of
`RewriteEvidence`. Use `occurrence.Before` / `After` for semantic expressions,
`occurrence.Kept` for retained captures, and `occurrence.Rewrite` for the previous
raw evidence (`Inputs`, syntax and semantic models). Rebuild rule bundles after
upgrading from preview.15.

`Fix.For(comment).Remove(preserveLines: true).Propose()` keeps original line breaks
while removing comment text. The default still deletes standalone comment lines.
Both modes require unchanged parsing and compiler-supplied arguments in every
affected loaded context.

## Behavior changes

- Reporting an omitted default argument (from `ArgumentsFor(name)`) reports at its
  call instead of failing the run.
- `CodeBranch.IsElseIf` is true only for an `else if` continuation, not for an `if`
  nested directly in a then-branch.
- Findings that `ReportAt` moves into generated or non-target source are suppressed,
  like findings on generated candidates; they no longer fail the run.
- `Code.TypeReferences` includes `void`, and `nint`/`nuint` match `IntPtr`/`UIntPtr`
  in every query form. Global aliases declared in generated files are recognized.
- `CodeProperty.IsAutoProperty` requires a compiler-supplied backing field, so
  abstract, interface, extern and partial-definition properties are excluded.

## New capabilities

- `Code.Fields`, `Code.Properties` and `Code.Parameters` with typed facts.
- `Code.TypeReferences` and `CodeType.References` for every written type usage.
- `Code.ObjectCreations` and `.Of<T>()` for explicit and target-typed `new`.
- `Code.Catches` for catch clauses, filters, empty handlers and rethrows.
- `Code.Comments` and `HasDocumentationComment()`.
- `InTestProjects()` and glob project scopes.
- `CodeExpression.MayBeNull` and `FlowState`.
- `CodeMethod.Parameters`, `Body()`, `ReturnsVoid`, `ReturnTypeIs` and `IsStatic`.
- `CodeTypeDefinition.IsClass`, `IsInterface`, `IsStruct`, `IsEnum`, `IsRecord`, `IsAbstract`, `IsStatic` and `IsNested`.
- `RuleTestResult.Output`, the exact CLI rendering, for complete-output test assertions.
- `declaration.ExplicitModifier(modifier)` to report at a written modifier token.
- `element.IsInFolder(folder)` beside `IsInProject` and `IsInNamespace`.
- `CodeIfStatement.Branches`, `statements.Branches()` and `branches.WithoutBraces()`.
- `drillpress install-skill` installs the version-matched [agent skill](AGENT_SKILL.md).
