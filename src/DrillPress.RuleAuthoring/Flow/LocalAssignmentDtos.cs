using Microsoft.CodeAnalysis;

namespace DrillPress;

internal sealed record LocalAssignmentEvidence(bool IsResolved, IReadOnlySet<ISymbol> Writes);
