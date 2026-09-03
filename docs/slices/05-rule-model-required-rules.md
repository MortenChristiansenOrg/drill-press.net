# Slice 5: rule model and required rules

This draft PR expands the single-rule vertical into the intended reusable
authoring model and implements every rule required to validate the architecture.

## Goal

Allow rule authors to express syntax, semantic, method-level, and
cross-project conventions with readable C# definitions rather than engine base
classes. The same model must support reusable xUnit-test selection, composed
conditions, explicit exceptions, precise locations, and optional fixes.

## Implementation scope

- Implement `Code.Methods`, `Code.Interfaces`, `Code.Types`, and
  `Code.MemberReferences` queries over one `AnalysisSolution`.
- Implement reusable `Where` selections and `And`, `Or`, `Not`, and
  `ExceptWhen` condition composition.
- Implement rule descriptors, requirement/forbid semantics, location
  selectors, and optional fix factories.
- Add focused `CodeType` support for common strongly typed BCL and
  constructed-generic identities, named target types, and optional assembly
  qualification.
- Keep semantic models and discovered model collections lazy within an
  analysis.
- Implement the five required diagnostics and their reusable query components.
- Attach the two requested fixes to their rules; Slice 6 is responsible for
  applying them safely.

The required diagnostics are:

- DP1001: xUnit tests have at most two empty lines.
- DP1002: assertions occur after the final empty line, except that a sole
  `Assert.Throws` may occur earlier.
- DP1003: an interface does not have exactly one concrete non-test
  implementation.
- DP1004: do not use `string.Empty`.
- DP1005: do not pass `StringComparer.Ordinal`.

## Acceptance criteria

- [ ] **s5.1** Rule definitions use high-level queries and conditions without
  inheriting from syntax-, symbol-, or operation-specific engine classes.
- [ ] **s5.2** A reusable xUnit-test query is declared once and shared by
  DP1001 and DP1002.
- [ ] **s5.3** `And`, `Or`, `Not`, and `ExceptWhen` preserve correct
  condition semantics and do not introduce unsafe candidate constraints.
- [ ] **s5.4** Every rule declares a unique non-empty ID, a concise remediation
  message, a condition, an optional location selector, and an optional fix;
  invalid registration fails clearly.
- [ ] **s5.5** Generic helpers support the required BCL types and at least one
  constructed generic such as `List<string>`.
- [ ] **s5.6** Named metadata identities match target-project types without a
  rule-to-target project reference and can optionally include assembly
  identity.
- [ ] **s5.7** Type identity uses no assembly scanning or dynamic rule
  discovery and works in the NativeAOT bundle.
- [ ] **s5.8** DP1001 reports the first empty line beyond the allowed two.
- [ ] **s5.9** DP1002 reports the first assertion before the final empty line.
- [ ] **s5.10** DP1002 exempts a sole `Assert.Throws` but not another assertion
  or a method containing multiple assertions.
- [ ] **s5.11** DP1003 reports an interface with exactly one concrete
  implementation outside evaluated test projects.
- [ ] **s5.12** DP1004 and DP1005 match semantic member identity rather than
  source spelling, including aliases and qualified access.
- [ ] **s5.13** DP1004 proposes replacement with `""`.
- [ ] **s5.14** DP1005 proposes argument removal only when speculative binding
  proves the rewritten invocation valid.
- [ ] **s5.15** Positive, negative, alias, conditional-source, multi-project,
  multi-target, and exception tests cover the applicable rules.
- [ ] **s5.16** Generated documents produce no rule candidates or diagnostics.
- [ ] **s5.17** Public authoring documentation includes concise examples an LLM
  can imitate without depending on engine internals.

## Dependencies

Depends on #3 for aggregation and output semantics and #4 for the
compiler-faithful multi-project model on which these rules operate.
