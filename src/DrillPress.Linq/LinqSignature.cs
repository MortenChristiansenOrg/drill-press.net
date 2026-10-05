using Microsoft.CodeAnalysis;

namespace DrillPress.Presets;

internal static class LinqSignature
{
    public static string Of(IMethodSymbol method)
    {
        var declaration = method.OriginalDefinition;
        return declaration.GetDocumentationCommentId()
            + ":"
            + declaration.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }
}
