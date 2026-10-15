using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CompilerSnapshot.Fixtures;

// Mirrors EF Core's DbSet suppressor: FixtureSet properties are assigned by their host.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FixtureSetSuppressor : DiagnosticSuppressor
{
    private static readonly SuppressionDescriptor Uninitialized = new SuppressionDescriptor(
        "FIXTURE0001",
        "CS8618",
        "FixtureSet properties are initialized by their host."
    );

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions =>
        ImmutableArray.Create(Uninitialized);

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            var tree = diagnostic.Location.SourceTree;
            if (tree == null)
                continue;
            var property =
                tree.GetRoot(context.CancellationToken).FindNode(diagnostic.Location.SourceSpan)
                as PropertyDeclarationSyntax;
            var symbol =
                property == null
                    ? null
                    : context
                        .GetSemanticModel(tree)
                        .GetDeclaredSymbol(property, context.CancellationToken) as IPropertySymbol;
            if (symbol != null && symbol.Type.Name == "FixtureSet")
                context.ReportSuppression(Suppression.Create(Uninitialized, diagnostic));
        }
    }
}
