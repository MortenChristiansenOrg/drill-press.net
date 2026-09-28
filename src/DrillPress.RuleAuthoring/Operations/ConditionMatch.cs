namespace DrillPress.Operations;

/// <summary>Evidence for one configured check; it does not prove redundancy or authorize deleting a guard.</summary>
public sealed class ConditionMatch : ICodeElement
{
    internal ConditionMatch(
        CodeCondition condition,
        ConditionPattern pattern,
        CodeExpression value,
        bool outcome
    )
    {
        Condition = condition;
        Pattern = pattern;
        CheckedValue = value;
        MatchingOutcome = outcome;
    }

    /// <summary>The complete conditional construct.</summary>
    public CodeCondition Condition { get; }

    /// <summary>The matched consumer classification and recognizer.</summary>
    public ConditionPattern Pattern { get; }

    /// <summary>The bound operand whose property/attributes can be inspected.</summary>
    public CodeExpression CheckedValue { get; }

    /// <summary>The branch outcome corresponding to the configured check's meaning.</summary>
    public bool MatchingOutcome { get; }

    /// <summary>The original compiler membership.</summary>
    public AnalysisSource Source => Condition.Source;

    /// <summary>The complete checked condition span.</summary>
    public SourceLocation Location => Condition.Location;

    /// <summary>The existing branch for the matched outcome, with nested functions excluded.</summary>
    public CodeBody? MatchingBranch => Condition.Branch(MatchingOutcome);
}
