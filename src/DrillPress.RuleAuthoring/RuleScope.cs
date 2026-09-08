namespace DrillPress;

/// <summary>Registers readable requirements and prohibitions over a reusable selection.</summary>
public sealed class RuleScope<T>(RuleSet ruleSet, CodeQuery<T> query)
{
    /// <summary>Reports every selected candidate, optionally selecting a precise span and proposing a complete fix.</summary>
    public RuleScope<T> Forbid(string id, string message, Func<T, SourceLocation>? location = null,
        Func<T, FixProposal?>? fix = null)
    {
        return Forbid(new RuleDescriptor(id, message), location, fix);
    }

    /// <summary>Registers a reusable descriptor with optional location and fix factories.</summary>
    public RuleScope<T> Forbid(RuleDescriptor descriptor, Func<T, SourceLocation>? location = null, Func<T, FixProposal?>? fix = null)
    {
        ruleSet.Add(query, descriptor, location, fix);
        return this;
    }

    /// <summary>Reports selected candidates that fail the required condition.</summary>
    public RuleScope<T> Require(RuleCondition<T> condition, string id, string message,
        Func<T, SourceLocation>? location = null, Func<T, FixProposal?>? fix = null)
    {
        ruleSet.Add(query.Where(condition.Not()), new RuleDescriptor(id, message), location, fix);
        return this;
    }
}
