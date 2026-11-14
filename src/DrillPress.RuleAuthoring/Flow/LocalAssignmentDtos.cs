using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

internal sealed record LocalAssignmentEvidence(bool IsResolved, IReadOnlySet<ISymbol> Writes);

// The assigned values that can reach a read, and whether a parameter's value on entry can also reach it.
internal sealed record ReachingValues(IReadOnlyList<ExpressionSyntax> Assigned, bool IncludesEntry);
