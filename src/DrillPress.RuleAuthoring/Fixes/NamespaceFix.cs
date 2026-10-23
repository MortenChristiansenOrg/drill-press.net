using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>Structured edits for a written block namespace.</summary>
public sealed class NamespaceFix
{
    private readonly CodeNode<NamespaceDeclarationSyntax> _declaration;

    internal NamespaceFix(CodeNode<NamespaceDeclarationSyntax> declaration) =>
        _declaration = declaration;

    /// <summary>Converts the file's single block namespace to file-scoped form with a library-owned structural proof.</summary>
    public FileScopedNamespaceConversion UseFileScopedNamespace() => new(_declaration);
}
