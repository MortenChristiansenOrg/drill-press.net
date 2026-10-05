using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

internal sealed record CoverageRange(
    TextSpan Span,
    ExecutionCoverage State,
    string FunctionIdentity
);
