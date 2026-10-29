using Microsoft.CodeAnalysis;

namespace DrillPress.Engine;

internal sealed class GeneratorTreeOptions(
    GeneratorOptions options,
    SyntaxTreeOptionsProvider? original
) : SyntaxTreeOptionsProvider
{
    public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken)
    {
        var value = original?.IsGenerated(tree, cancellationToken) ?? GeneratedKind.Unknown;
        if (value != GeneratedKind.Unknown)
            return value;
        return
            options
                .For(tree.FilePath)
                .AnalyzerOptions.TryGetValue("generated_code", out var generated)
            && bool.TryParse(generated, out var flag)
            ? flag
                ? GeneratedKind.MarkedGenerated
                : GeneratedKind.NotGenerated
            : GeneratedKind.Unknown;
    }

    public override bool TryGetDiagnosticValue(
        SyntaxTree tree,
        string id,
        CancellationToken cancellationToken,
        out ReportDiagnostic value
    )
    {
        if (original?.TryGetDiagnosticValue(tree, id, cancellationToken, out value) == true)
            return true;
        return options.For(tree.FilePath).TreeOptions.TryGetValue(id, out value);
    }

    public override bool TryGetGlobalDiagnosticValue(
        string id,
        CancellationToken cancellationToken,
        out ReportDiagnostic value
    )
    {
        if (original?.TryGetGlobalDiagnosticValue(id, cancellationToken, out value) == true)
            return true;
        return options.GlobalDiagnostic(id, out value);
    }
}
