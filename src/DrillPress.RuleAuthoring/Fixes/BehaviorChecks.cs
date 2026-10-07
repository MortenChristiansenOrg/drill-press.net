namespace DrillPress;

internal static class BehaviorChecks
{
    internal static IEnumerable<Func<RewriteEvidence, ProofResult>> Expressions(
        ExpressionBehavior behavior
    )
    {
        const ExpressionBehavior supported =
            ExpressionBehavior.EvaluationCounts
            | ExpressionBehavior.EvaluationOrder
            | ExpressionBehavior.NullReceiverBehavior;
        if (behavior == 0 || (behavior & ~supported) != 0)
            throw new ArgumentOutOfRangeException(nameof(behavior));
        if (behavior.HasFlag(ExpressionBehavior.EvaluationCounts))
            yield return RewriteChecks.SameEvaluationCounts;
        if (behavior.HasFlag(ExpressionBehavior.EvaluationOrder))
            yield return RewriteChecks.SameEvaluationSequence;
        if (behavior.HasFlag(ExpressionBehavior.NullReceiverBehavior))
            yield return RewriteChecks.SameReceiverNullBehavior;
    }

    internal static IEnumerable<Func<ModifierChange, ProofResult>> Declarations(
        DeclarationBehavior behavior
    )
    {
        const DeclarationBehavior supported =
            DeclarationBehavior.Accessibility
            | DeclarationBehavior.ContainingAccessibility
            | DeclarationBehavior.Identity
            | DeclarationBehavior.Contract;
        if (behavior == 0 || (behavior & ~supported) != 0)
            throw new ArgumentOutOfRangeException(nameof(behavior));
        if (behavior.HasFlag(DeclarationBehavior.Accessibility))
            yield return DeclarationChecks.SameDeclaredAccessibility;
        if (behavior.HasFlag(DeclarationBehavior.ContainingAccessibility))
            yield return DeclarationChecks.SameContainingAccessibility;
        if (behavior.HasFlag(DeclarationBehavior.Identity))
            yield return DeclarationChecks.SameIdentity;
        if (behavior.HasFlag(DeclarationBehavior.Contract))
            yield return DeclarationChecks.SameContract;
    }
}
