namespace DrillPress;

/// <summary>One reserved rule identity. Add a clause per candidate kind with <see cref="For{T}(CodeQuery{T})"/>; every clause reports under the same ID and message.</summary>
public sealed class RuleDefinition
{
    private readonly RuleRegistration _registration;

    internal RuleDefinition(RuleRegistration registration) => _registration = registration;

    /// <summary>The stable identity, remediation and optional fix effort shared by every clause.</summary>
    public RuleDescriptor Descriptor => _registration.Descriptor;

    /// <summary>Begins a clause over the selected candidates. End it with <see cref="RuleClause{T}.Forbid"/> or <see cref="RuleClause{T}.Require(Func{T, bool}, Func{T, FixProposal?}?)"/>.</summary>
    public RuleClause<T> For<T>(CodeQuery<T> query) => new(this, _registration, query);
}
