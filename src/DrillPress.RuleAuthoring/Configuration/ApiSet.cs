using DrillPress.Semantics;
using Microsoft.CodeAnalysis;

namespace DrillPress.Configuration;

/// <summary>An immutable configured collection of method identities, reusable across restrictions and call-path rules.</summary>
public sealed class ApiSet(params CodeMember[] members)
{
    private readonly CodeMember[] _members = members.ToArray();

    /// <summary>Matches any configured overload or method family.</summary>
    public bool Contains(IMethodSymbol method) => _members.Any(member => member.Matches(method));
}
