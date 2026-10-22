namespace DrillPress;

/// <summary>Selections for standard collection initialization with a written target type.</summary>
public static class CollectionExpressionQueries
{
    /// <summary>Selects parameterless List, HashSet and Dictionary creation, rank-one arrays and Array.Empty calls in target-typed positions. Interface targets remain candidates but their automatic fixes are withheld if the concrete type would change.</summary>
    public static CodeQuery<CodeExpression> WithCollectionExpressionTarget(
        this CodeQuery<CodeExpression> expressions
    ) =>
        expressions.Where(expression =>
            CollectionExpressionConversion.IsCollectionInitialisation(expression.Syntax)
            && CollectionExpressionConversion.IsTargetTyped(expression.Syntax)
            && CollectionExpressionConversion.CreatesCollection(expression)
            && CollectionExpressionConversion.AcceptsCollectionExpression(expression)
        );
}
