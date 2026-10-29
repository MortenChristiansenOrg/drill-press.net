using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DrillPress.Engine;

[SuppressMessage(
    "MicrosoftCodeAnalysisCorrectness",
    "RS1001",
    Justification = "An internal callback scheduler, instantiated directly; it must not be discovered as a compiler plug-in."
)]
internal sealed class SemanticPreparationAnalyzer(Action<SemanticModelAnalysisContext> prepared)
    : DiagnosticAnalyzer
{
    [SuppressMessage(
        "MicrosoftCodeAnalysisReleaseTracking",
        "RS2008",
        Justification = "This private descriptor schedules compiler callbacks; it never reports a diagnostic."
    )]
    private static readonly DiagnosticDescriptor _descriptor = new(
        "DPGEN001",
        "Prepare semantic evidence",
        "Prepare semantic evidence",
        "DrillPress",
        DiagnosticSeverity.Hidden,
        true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [_descriptor];

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.RegisterSemanticModelAction(prepared);
    }
}
