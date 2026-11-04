namespace DrillPress;

/// <summary>An immutable inlining of a local that only renames its initializer: the local's single read becomes the initializer's original text, parenthesized only where needed, and the declaration's lines are removed.</summary>
/// <remarks>
/// Gates: a non-constant, non-ref, non-scoped, non-using local declaration statement directly in a block or switch section, in editable ordinary source, declaring one variable with an initializer; no comments or directives on the declaration and no code sharing its lines; an initializer that declares no variables, is not target-typed and has exactly the local's type, ignoring nullable annotations; a local that is never written or passed by reference and is referenced exactly once, outside nameof and expression trees; and a read in the header of a later statement of the same list (an expression statement, return, throw, yield return, local declaration, or the condition, governing expression, collection, resource or lock expression), evaluated once and unconditionally there: not deferred in a lambda, local function or query, not inside checked or unchecked, not after <c>&amp;&amp;</c>, <c>||</c> or <c>??</c>, in a conditional branch, conditional access, switch expression arm or interpolated string handler, and not part of a [Conditional] or unimplemented partial method call. A struct local used as a receiver withholds the fix when its initializer is a variable, which the receiver would then mutate.
/// In every affected compilation the enclosing expressions keep their bindings, every expression of the moved initializer keeps its syntax, symbol, type and conversion and its root its natural type, the read's nullable state is unchanged, and compiler-supplied arguments such as caller line numbers are unchanged.
/// </remarks>
public sealed class LocalInlining
{
    private readonly CodeVariableDeclaration _declaration;

    internal LocalInlining(CodeVariableDeclaration declaration) => _declaration = declaration;

    /// <summary>Proposes the inlining when the initializer moves past nothing observable: the read is in the statement right after the declaration, and that statement evaluates only literals, constants, <c>this</c>, unchanged non-ref locals and parameters that nothing aliases, instance readonly fields outside constructors and safe implicit conversions before the read; a constructor or copy whose initializer contains the read also runs first. The library owns this proof.</summary>
    public FixProposal? Propose() => Plan(ordered: true, _ => true);

    /// <summary>Proposes the inlining when your proof holds in every affected compilation. The proof waives only the evaluation order: the read may follow other statements of the same block, and its statement may evaluate calls, mutable fields or other operands before it. <see cref="InlineChange.SkippedStatements"/> and <see cref="InlineChange.SkippedOperands"/> list what the initializer now follows; state why that cannot observe or change its result or effects. Every other gate still applies.</summary>
    public FixProposal? SafeWhen(Func<InlineChange, bool> proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        return Plan(ordered: false, proof);
    }

    private FixProposal Plan(bool ordered, Func<InlineChange, bool> proof)
    {
        var plan = LocalInliningPlan.Create(
            _declaration.Source,
            _declaration.Syntax,
            ordered,
            out var reason
        );
        if (plan is null)
            return FixProposal.Withheld(reason);
        var edits = plan.Edits;
        var declaration = plan.Declaration.Declaration.Span;
        return SourceChanges.ProposeExplained(
            edits,
            context =>
                LocalInliningValidation.Validate(context, plan, declaration, ordered, proof, edits)
        );
    }
}
