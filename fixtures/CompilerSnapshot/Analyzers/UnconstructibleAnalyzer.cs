using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CompilerSnapshot.Fixtures;

// Real builds report analyzers that cannot be constructed as warning CS8032.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnconstructibleAnalyzer : DiagnosticAnalyzer
{
    public UnconstructibleAnalyzer() =>
        throw new InvalidOperationException("Requested analyzer construction failure.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray<DiagnosticDescriptor>.Empty;

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
    }
}
