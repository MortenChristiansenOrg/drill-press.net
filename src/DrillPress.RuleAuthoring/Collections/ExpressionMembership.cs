using DrillPress.Operations;

namespace DrillPress.Collections;

/// <summary>An expression and every selected group containing that occurrence in the same compilation.</summary>
public sealed class ExpressionMembership : ICodeElement
{
    internal ExpressionMembership(CodeExpression expression, IReadOnlyList<ExpressionGroup> groups)
    {
        Expression = expression;
        Groups = groups;
    }

    /// <summary>The original candidate, retained even if no unique group exists.</summary>
    public CodeExpression Expression { get; }

    /// <summary>All group memberships in selection order.</summary>
    public IReadOnlyList<ExpressionGroup> Groups { get; }

    /// <summary>The sole matching group, or null for missing or ambiguous membership.</summary>
    public ExpressionGroup? UniqueGroup => Groups.Count == 1 ? Groups[0] : null;

    /// <summary>The expression's compilation membership.</summary>
    public AnalysisSource Source => Expression.Source;

    /// <summary>The expression's reportable span.</summary>
    public SourceLocation Location => Expression.Location;
}
