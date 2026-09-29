namespace DrillPress;

internal static class BehaviorChecks
{
    internal static IEnumerable<Func<RewriteEvidence, ProofResult>> Expressions(Behavior behavior)
    {
        const Behavior supported =
            Behavior.Bindings
            | Behavior.EvaluationCounts
            | Behavior.EvaluationSequence
            | Behavior.NullReceiverBehavior;
        if ((behavior & ~supported) != 0)
            throw new ArgumentOutOfRangeException(nameof(behavior));
        if (behavior.HasFlag(Behavior.Bindings))
            yield return RewriteChecks.SameRetainedBindings;
        if (behavior.HasFlag(Behavior.EvaluationCounts))
            yield return RewriteChecks.SameEvaluationCounts;
        if (behavior.HasFlag(Behavior.EvaluationSequence))
            yield return RewriteChecks.SameEvaluationSequence;
        if (behavior.HasFlag(Behavior.NullReceiverBehavior))
            yield return RewriteChecks.SameReceiverNullBehavior;
    }

    internal static IEnumerable<Func<DeclarationRewrite, ProofResult>> Declarations(
        Behavior behavior
    )
    {
        const Behavior supported = Behavior.Accessibility | Behavior.Identity | Behavior.Contract;
        if ((behavior & ~supported) != 0)
            throw new ArgumentOutOfRangeException(nameof(behavior));
        if (behavior.HasFlag(Behavior.Accessibility))
            yield return DeclarationChecks.SameDeclaredAccessibility;
        if (behavior.HasFlag(Behavior.Identity))
            yield return DeclarationChecks.SameIdentity;
        if (behavior.HasFlag(Behavior.Contract))
            yield return DeclarationChecks.SameContract;
    }
}
