namespace DrillPress;

/// <summary>Owns one reserved rule identity and composes independently typed clauses under its descriptor.</summary>
public sealed class RuleDefinition
{
    private readonly RuleSet _ruleSet;
    private readonly RuleRegistration _registration;

    internal RuleDefinition(RuleSet ruleSet, RuleRegistration registration) =>
        (_ruleSet, _registration) = (ruleSet, registration);

    /// <summary>The stable identity, remediation and optional fix effort shared by every clause.</summary>
    public RuleDescriptor Descriptor => _registration.Descriptor;

    /// <summary>Begins a typed clause with its own selection, requirement, reporting group and fix factories.</summary>
    public RuleClause<T> For<T>(CodeQuery<T> query) =>
        new(new RuleScope<T>(_ruleSet, query) { Registration = _registration }, Descriptor);
}
