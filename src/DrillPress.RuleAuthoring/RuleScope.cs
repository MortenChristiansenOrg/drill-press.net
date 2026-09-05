namespace DrillPress;

/// <summary>Builds rules over one selected candidate type.</summary>
public sealed class RuleScope<T>(RuleSet ruleSet, CodeQuery<T> query)
{
    /// <summary>Registers a diagnostic for every candidate selected by the query.</summary>
    /// <param name="id">The stable rule identifier.</param>
    /// <param name="message">Concise guidance for correcting the violation.</param>
    public RuleScope<T> Forbid(string id, string message)
    {
        ruleSet.Add(query, id, message);
        return this;
    }
}
