using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>Solution-wide evidence that sealing a class or making it static changes nothing observable in loaded code.</summary>
internal static class TypeUsage
{
    /// <summary>Why the class cannot become sealed or static, or null when no loaded project depends on it being otherwise.</summary>
    internal static string? Explain(
        AnalysisProject project,
        INamedTypeSymbol type,
        SyntaxKind modifier
    )
    {
        if (
            type.TypeKind != TypeKind.Class
            || type.IsRecord && modifier == SyntaxKind.StaticKeyword
        )
            return "the library proves only sealed and static classes";
        if (type.IsAbstract || type.IsStatic)
            return "the class is abstract or static";
        var projects = project.SolutionOrNull?.Projects ?? [project];
        var identity = RewriteSymbols.Identity(type);
        foreach (
            var candidate in projects.SelectMany(loaded =>
                Types(loaded.Compilation.Assembly.GlobalNamespace)
            )
        )
            for (
                var baseType = candidate.BaseType;
                baseType is not null;
                baseType = baseType.BaseType
            )
                if (RewriteSymbols.Identity(baseType.OriginalDefinition) == identity)
                    return $"{candidate.ToDisplayString()} derives from the class";
        return modifier == SyntaxKind.StaticKeyword ? Instances(projects, type, identity) : null;
    }

    private static string? Instances(
        IReadOnlyList<AnalysisProject> projects,
        INamedTypeSymbol type,
        string? identity
    )
    {
        if (type.BaseType?.SpecialType != SpecialType.System_Object)
            return "the class derives from another class";
        if (type.Interfaces.Length > 0)
            return "the class implements an interface";
        foreach (var member in type.GetMembers())
        {
            if (member is INamedTypeSymbol || member.IsStatic)
                continue;
            if (member is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
            {
                if (!constructor.IsImplicitlyDeclared)
                    return "the class declares an instance constructor";
                continue;
            }
            return $"{member.Name} is an instance member";
        }
        foreach (var source in projects.SelectMany(project => project.Sources))
        foreach (
            var name in source
                .Tree.GetRoot(source.Project.CancellationToken)
                .DescendantNodes()
                .OfType<SimpleNameSyntax>()
                .Where(name => name.Identifier.ValueText == type.Name)
        )
        {
            if (
                RewriteSymbols.Identity(
                    source
                        .Model.GetSymbolInfo(name, source.Project.CancellationToken)
                        .Symbol?.OriginalDefinition
                ) != identity
            )
                continue;
            SyntaxNode use = name;
            while (
                use.Parent is QualifiedNameSyntax qualified && qualified.Right == use
                || use.Parent is MemberAccessExpressionSyntax access && access.Name == use
                || use.Parent is AliasQualifiedNameSyntax alias && alias.Name == use
            )
                use = use.Parent;
            if (!IsMemberAccess(use))
                return $"{FixReasons.Describe(source, use)} in {Path.GetFileName(source.Document.Path)} uses the class as a type";
        }
        return null;
    }

    // Static classes allow only access to their members, nameof and using static.
    private static bool IsMemberAccess(SyntaxNode use) =>
        use.Parent switch
        {
            MemberAccessExpressionSyntax access => access.Expression == use,
            QualifiedNameSyntax qualified => qualified.Left == use,
            UsingDirectiveSyntax => true,
            ArgumentSyntax
            {
                Parent.Parent: InvocationExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" }
                }
            } => true,
            _ => use.AncestorsAndSelf().Any(node => node is UsingDirectiveSyntax),
        };

    private static IEnumerable<INamedTypeSymbol> Types(INamespaceOrTypeSymbol container)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceSymbol nested)
                foreach (var type in Types(nested))
                    yield return type;
            else if (member is INamedTypeSymbol type)
            {
                yield return type;
                foreach (var inner in Types(type))
                    yield return inner;
            }
        }
    }
}
