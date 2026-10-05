using DrillPress.Manifest;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress.Testing;

internal sealed record TestExecutionFact(
    string ContextId,
    DocumentSnapshot Document,
    TextSpan Span,
    ExecutionCoverage State,
    CoverageReason[] Reasons,
    CoverageMetric Metric = CoverageMetric.Execution
);

internal sealed record TestLineFact(
    string ContextId,
    DocumentSnapshot Document,
    LineCoverageMeasurement Measurement
);
