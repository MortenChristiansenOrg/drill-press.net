using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>Source declaration shapes and name-prefiltered type uses, independent of repository naming policy.</summary>
public static class DeclarationFacts
{
    /// <summary>The fully qualified original declaration display name, ignoring constructed type arguments.</summary>
    public static string Key(INamedTypeSymbol type) =>
        type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>Whether syntax is a class with only automatic properties/fields and no primary constructor. Record and enum policy belongs to the consumer.</summary>
    public static bool IsDataClass(BaseTypeDeclarationSyntax type) =>
        type is ClassDeclarationSyntax { ParameterList: null } data
        && data.Members.All(member =>
            member is PropertyDeclarationSyntax { ExpressionBody: null } or FieldDeclarationSyntax
        )
        && data.Members.OfType<PropertyDeclarationSyntax>()
            .All(property =>
                property.AccessorList?.Accessors.All(accessor =>
                    accessor.Body is null && accessor.ExpressionBody is null
                ) != false
            );

    // A class whose declared members are all static, so it holds no state and no instance is needed.
    /// <summary>Whether a non-static concrete class declares only static members or nested types, inherits object and implements no interface.</summary>
    public static bool HasOnlyStaticMembers(CodeTypeDefinition type) =>
        type.IsClass
        && !type.IsRecord
        && !type.IsStatic
        && !type.IsAbstract
        && type.Syntax is ClassDeclarationSyntax
        && type.Symbol.BaseType?.SpecialType == SpecialType.System_Object
        && type.Symbol.Interfaces.Length == 0
        && type.Symbol.GetMembers().Where(member => !member.IsImplicitlyDeclared) is var members
        && members.Any()
        && members.All(member => member.IsStatic || member is INamedTypeSymbol);

    // The class of a program's top-level statements, which a test host names as WebApplicationFactory<Program>.
    /// <summary>Whether a type contains the compiler-generated top-level entry point.</summary>
    public static bool IsTopLevelProgram(CodeTypeDefinition type) =>
        type.Symbol.GetMembers().Any(member => member is IMethodSymbol { Name: "<Main>$" });

    // The project directory, relative to which the file's folders are read; the path is null for files outside it.
    /// <summary>The source path relative to the project directory, using forward slashes; null outside it.</summary>
    public static string? RelativePath(AnalysisSource source)
    {
        var path = source.Document.Path.Replace('\\', '/');
        var directory = Path.GetDirectoryName(source.Project.ProjectPath)?.Replace('\\', '/') ?? "";
        if (directory.Length == 0)
        {
            return path;
        }
        return path.StartsWith(directory + "/", StringComparison.Ordinal)
            ? path[(directory.Length + 1)..]
            : null;
    }

    /// <summary>The project name followed by relative folder segments; null for a file outside its project directory.</summary>
    public static string? NamespaceFromFolders(AnalysisSource source) =>
        RelativePath(source) is { } path
            ? string.Join('.', [source.Project.Name, .. path.Split('/')[..^1]])
            : null;

    /// <summary>Written types directly in the compilation unit or a namespace, excluding nested types.</summary>
    public static List<BaseTypeDeclarationSyntax> TopLevelTypes(AnalysisSource source) =>
        source
            .Tree.GetRoot(source.Project.CancellationToken)
            .DescendantNodes(node =>
                node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax
            )
            .OfType<BaseTypeDeclarationSyntax>()
            .ToList();

    // The names in source that refer to a type. Only files whose text contains its name are bound, which is much faster than
    // binding every type reference in the solution.
    /// <summary>Name-prefiltered bound type references across loaded ordinary sources; compares original declaration display names and excludes aliases whose spelling differs.</summary>
    public static IEnumerable<(AnalysisSource Source, SimpleNameSyntax Name)> UsesOf(
        AnalysisSolution solution,
        INamedTypeSymbol type
    )
    {
        var key = Key(type);
        var attributeName = type.Name.EndsWith("Attribute", StringComparison.Ordinal)
            ? type.Name[..^"Attribute".Length]
            : null;
        foreach (var source in solution.Projects.SelectMany(project => project.Sources))
        {
            if (
                source.Document.IsGenerated
                || !source.Document.Text.Contains(
                    attributeName ?? type.Name,
                    StringComparison.Ordinal
                )
            )
            {
                continue;
            }
            var names = source
                .Tree.GetRoot(source.Project.CancellationToken)
                .DescendantNodes()
                .OfType<SimpleNameSyntax>()
                .Where(name =>
                    name.Identifier.ValueText == type.Name
                    || name.Identifier.ValueText == attributeName
                );
            foreach (var name in names)
            {
                var symbol = source
                    .Model.GetSymbolInfo(name, source.Project.CancellationToken)
                    .Symbol;
                var referenced = symbol
                    is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
                    ? constructor.ContainingType
                    : symbol as INamedTypeSymbol;
                if (referenced is not null && Key(referenced) == key)
                {
                    yield return (source, name);
                }
            }
        }
    }

    /// <summary>Whether a type has a visible name-based use in another source file.</summary>
    public static bool IsUsedOutside(INamedTypeSymbol type, AnalysisSource declaring) =>
        UsesOf(declaring.Solution, type)
            .Any(use => use.Source.Document.Path != declaring.Document.Path);

    // A static class can't be a type argument, such as the category of an ILogger<T>.
    /// <summary>Whether a visible name-based type use appears in a generic argument list.</summary>
    public static bool IsTypeArgument(AnalysisSolution solution, INamedTypeSymbol type) =>
        UsesOf(solution, type)
            .Any(use =>
                use.Name.AncestorsAndSelf()
                    .TakeWhile(node => node is TypeSyntax)
                    .Any(node => node.Parent is TypeArgumentListSyntax)
            );
}
