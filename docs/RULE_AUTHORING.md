# Authoring compiled rules

A rule bundle constructs a `RuleSet` in ordinary C# and passes it to
`RuleApplication`. Rules are compiled into the managed or NativeAOT executable;
there is no runtime source compiler, assembly scan, or rule discovery.

```csharp
var rules = new RuleSet();
var tests = XunitTests.Methods;

rules.For(tests).Require(
    new(method => method.Body.EmptyLines.Count <= 2),
    "DP1001", "Keep at most two empty lines in a test.",
    method => method.Body.EmptyLines[2]);

rules.For(Code.MemberReferences.Where(Members.Are<string>(nameof(string.Empty))))
    .Forbid("DP1004", "Use the empty string literal \"\" instead of string.Empty.",
        fix: EmptyStringFix.Create);
```

`Code.Methods`, `Code.Types`, `Code.Interfaces`, and `Code.MemberReferences` share
one `AnalysisSolution`. Collections and semantic models are created on first
use. Each `AnalysisProject` is a separate evaluated compilation context;
frameworks and conditional source are not combined. Generated source supplies
compiler semantics and implementation counts but never reportable candidates.

`Where` returns a reusable selection. `RuleCondition<T>.And`, `.Or`, `.Not`, and
`.ExceptWhen` compose Boolean predicates with short-circuit evaluation.
`query.ExceptWhen(condition)` excludes exceptions. Predicates should be pure:
the engine can share discovery and evaluate a selection more than once.

```csharp
var shortName = new RuleCondition<CodeMethod>(method => method.Symbol?.Name.Length < 3);
var constructorLike = new RuleCondition<CodeMethod>(method => method.Symbol?.Name == "New");
var candidates = Code.Methods.Where(shortName.Or(constructorLike)).ExceptWhen(XunitTests.AreTests);
rules.For(candidates).Forbid("TEAM001", "Use a descriptive method name.");
```

`Forbid` reports every selected candidate. `Require` reports only candidates
that fail its condition. Both accept a location selector and an optional fix
factory. The selector runs only for violations. Without one, methods/types use
the declaration identifier and references use the complete bound expression.
`RuleDescriptor` can be reused with `Forbid`. IDs must be unique and nonblank;
IDs and messages must be single-line. Registration rejects invalid descriptors.

Use semantic identities instead of source spelling:

```csharp
var target = CodeType.Named("Product.Storage.Cache", "Product.Storage");
var reads = Code.MemberReferences.Where(Members.Are(target, "Read"));
var lists = CodeType.Of<List<string>>(); // includes the constructed string argument
```

Assembly qualification can be a simple name or full assembly display identity.
An omitted qualification matches any declaring assembly. Use `Members.Are` or
`CodeType.Matches` for selection; record equality does not apply optional-assembly
matching. Strongly typed BCL
identities recognize framework reference-assembly facades. `Members.Are` matches
the member's declaring symbol, including aliases, qualified access, and static
imports. Ambiguous candidate symbols are not guessed.

The sample bundle supplies these conventions:

| Rule | Reported location and behavior |
| --- | --- |
| DP1001 | Third whitespace-only physical line in a block-bodied xUnit test. Multiline strings/comments, disabled text, and nested functions do not supply blank lines. |
| DP1002 | First resolved xUnit assertion before the last qualifying blank line. A sole synchronous `Assert.Throws` is exempt; `ThrowsAsync` and multiple assertions are not. |
| DP1003 | One ordinary declaration of an interface with exactly one concrete non-test source implementation in a compatible evaluated view. Partial/generic definitions count once; inherited and generated implementations count. Source dependencies participate; external metadata consumers do not. |
| DP1004 | A resolved `string.Empty` reference. A fix replaces only the expression span with `""`, retaining surrounding trivia. |
| DP1005 | A resolved `StringComparer.Ordinal` reference within an argument. Only the documented overload pair below currently receives a fix. |

xUnit selection recognizes Fact/Theory attributes in the xUnit core assemblies
and walks derived attribute base types. Assertions must resolve to xUnit's Assert
class. Similarly named unrelated attributes and assertion classes do not match.
Interface views combine compatible consumer roots for the same target framework;
alternate frameworks and incompatible dependency evaluations remain independent. A shared interface can consequently
violate the convention in one framework and satisfy it in another.

## Fix contracts

A fix factory returns `FixProposal`, containing every `SourceEdit` required by
one atomic correction and a context validation function. The engine invokes
that function for every loaded context containing an edited physical identity,
including contexts without a finding. Missing, inactive, generated, non-editable,
or ambiguously bound memberships cannot be assumed safe. The shared response
validator then requires agreement and withholds entire conflicting batches.
The `+` marker means that this complete plan survived validation; proposing a
fix does not write files.

The empty-string fix rejects `nameof`, expression trees, interior comments/directives, erroneous binding,
changed enclosing overloads, and changed conversions. In particular, converting
a nonconstant expression to a constant can change an overload selected several
expressions above the field reference. Rebinding only the immediate argument is
insufficient.

The comparer allowlist is exactly the BCL
`Enumerable.Distinct<string>(IEnumerable<string>, IEqualityComparer<string>)`
to `Enumerable.Distinct<string>(IEnumerable<string>)` pair. Static, extension,
and named arguments are mapped through compiler operations before removal;
the rewritten call and its enclosing expressions are rebound. Removed argument
trivia is preserved, which can leave an extra space. Other methods, constructors,
optional/params APIs, custom overloads, and expression-tree calls remain
non-fixable.

This equivalence follows from the [Distinct overload contract](https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.distinct?view=net-10.0)
and [ordinal string equality](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/strings/common-tasks/compare).
Ordering APIs can default to culture-sensitive comparison, and collection
constructors can expose comparer object identity, so successful rebinding alone
does not justify extending the allowlist.
