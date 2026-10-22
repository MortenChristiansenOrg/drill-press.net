namespace DrillPress;

internal static class BehaviorChecks
{
    internal static IEnumerable<(
        string Name,
        Func<RewriteEvidence, ProofResult> Prove
    )> Expressions(ExpressionBehavior behavior)
    {
        const ExpressionBehavior supported =
            ExpressionBehavior.EvaluationCounts
            | ExpressionBehavior.EvaluationOrder
            | ExpressionBehavior.NullReceiverBehavior;
        if (behavior == 0 || (behavior & ~supported) != 0)
            throw new ArgumentOutOfRangeException(nameof(behavior));
        if (behavior.HasFlag(ExpressionBehavior.EvaluationCounts))
            yield return ("evaluation count", RewriteChecks.SameEvaluationCounts);
        if (behavior.HasFlag(ExpressionBehavior.EvaluationOrder))
            yield return ("evaluation order", RewriteChecks.SameEvaluationSequence);
        if (behavior.HasFlag(ExpressionBehavior.NullReceiverBehavior))
            yield return ("null receiver", RewriteChecks.SameReceiverNullBehavior);
    }

    internal static IEnumerable<(
        string Name,
        Func<ModifierChange, ProofResult> Prove
    )> Declarations(DeclarationBehavior behavior)
    {
        const DeclarationBehavior supported =
            DeclarationBehavior.Accessibility
            | DeclarationBehavior.ContainingAccessibility
            | DeclarationBehavior.Identity
            | DeclarationBehavior.Contract;
        if (behavior == 0 || (behavior & ~supported) != 0)
            throw new ArgumentOutOfRangeException(nameof(behavior));
        if (behavior.HasFlag(DeclarationBehavior.Accessibility))
            yield return ("declared accessibility", DeclarationChecks.SameDeclaredAccessibility);
        if (behavior.HasFlag(DeclarationBehavior.ContainingAccessibility))
            yield return (
                "containing accessibility",
                DeclarationChecks.SameContainingAccessibility
            );
        if (behavior.HasFlag(DeclarationBehavior.Identity))
            yield return ("declaration identity", DeclarationChecks.SameIdentity);
        if (behavior.HasFlag(DeclarationBehavior.Contract))
            yield return ("declaration contract", DeclarationChecks.SameContract);
    }
}
