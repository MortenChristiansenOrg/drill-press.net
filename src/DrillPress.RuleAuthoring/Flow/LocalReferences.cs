using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

internal static class LocalReferences
{
    // The member that contains a local's declaration, or the file for top-level statements.
    internal static SyntaxNode Scope(AnalysisSource source, SyntaxNode declaration) =>
        declaration
            .Ancestors()
            .FirstOrDefault(node =>
                node
                    is MemberDeclarationSyntax
                        and not BaseTypeDeclarationSyntax
                        and not BaseNamespaceDeclarationSyntax
                        and not GlobalStatementSyntax
            )
        ?? source.Tree.GetRoot(source.Project.CancellationToken);

    // Every name binding to the local within its member, or the file for top-level statements.
    internal static IReadOnlyList<IdentifierNameSyntax> Find(
        AnalysisSource source,
        ILocalSymbol local,
        SyntaxNode declaration
    )
    {
        var scope = Scope(source, declaration);
        var references = new List<IdentifierNameSyntax>();
        foreach (var name in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            source.Project.CancellationToken.ThrowIfCancellationRequested();
            if (
                name.Identifier.ValueText == local.Name
                && SymbolEqualityComparer.Default.Equals(
                    source.Model.GetSymbolInfo(name, source.Project.CancellationToken).Symbol,
                    local
                )
            )
                references.Add(name);
        }
        return references.AsReadOnly();
    }

    // An initialized single-variable local declaration statement in a block or switch section, neither const,
    // ref nor using, whose local is never written or passed by reference, with its one reference.
    internal static IdentifierNameSyntax? SingleRead(
        AnalysisSource source,
        VariableDeclarationSyntax declaration
    ) =>
        declaration.Parent
            is LocalDeclarationStatementSyntax
            {
                Parent: BlockSyntax or SwitchSectionSyntax,
                UsingKeyword.RawKind: 0,
            } statement
        && !statement.IsConst
        && declaration.Variables is [{ Initializer: not null } declarator]
        && source.Model.GetDeclaredSymbol(declarator, source.Project.CancellationToken)
            is ILocalSymbol { RefKind: RefKind.None } local
        && source.LocalAssignments.Initializer(local) is not null
        && Find(source, local, declarator) is [var read]
            ? read
            : null;
}
