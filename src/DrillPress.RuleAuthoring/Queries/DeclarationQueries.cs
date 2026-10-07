using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress;

/// <summary>Name, attribute, modifier and accessibility predicates and filters shared by every declaration kind: types, type parts, methods, fields, properties, parameters and declared symbols.</summary>
public static class DeclarationQueries
{
    /// <summary>Tests an ordinal prefix on the declared name.</summary>
    public static bool NameStartsWith(this ICodeDeclaration declaration, string prefix) =>
        declaration.Name.StartsWith(prefix, StringComparison.Ordinal);

    /// <summary>Tests an ordinal suffix on the declared name.</summary>
    public static bool NameEndsWith(this ICodeDeclaration declaration, string suffix) =>
        declaration.Name.EndsWith(suffix, StringComparison.Ordinal);

    /// <summary>Tests a case-sensitive glob (* and ?) against the complete declared name.</summary>
    public static bool NameMatches(this ICodeDeclaration declaration, string pattern) =>
        new PathPattern(pattern).Matches(declaration.Name);

    /// <summary>Tests one complete identifier word, using ordinal comparison unless configured otherwise.</summary>
    public static bool NameContainsWord(
        this ICodeDeclaration declaration,
        string word,
        StringComparison comparison = StringComparison.Ordinal
    ) => CodeIdentifier.NamedContainsWord(declaration.Name, word, comparison);

    /// <summary>Attributes applied directly to the declared symbol; empty when binding fails. A written type part sees only attributes written in that part. Method attributes are not inherited from overridden methods.</summary>
    public static IEnumerable<CodeAttribute> Attributes(this ICodeDeclaration declaration)
    {
        var attributes = declaration.Symbol?.Attributes() ?? [];
        return declaration is CodeTypeDeclaration part
            ? attributes.Where(attribute =>
                attribute.Data.ApplicationSyntaxReference is { } written
                && written.SyntaxTree == part.Syntax.SyntaxTree
                && part.Syntax.Span.Contains(written.Span)
            )
            : attributes;
    }

    /// <summary>Tests an applied attribute by semantic identity, including attribute classes derived from the marker.</summary>
    public static bool HasAttribute(this ICodeDeclaration declaration, CodeType attribute) =>
        declaration.Attributes().Any(applied => applied.Matches(attribute));

    /// <summary>Tests an applied attribute and its compiler-recorded argument values.</summary>
    public static bool HasAttribute(
        this ICodeDeclaration declaration,
        CodeType attribute,
        Func<CodeAttribute, bool> where
    ) => declaration.Attributes().Any(applied => applied.Matches(attribute) && where(applied));

    /// <summary>Whether a <c>///</c> or <c>/** */</c> documentation comment precedes the written declaration; any documented part counts for a partial type definition.</summary>
    public static bool HasDocumentationComment(this ICodeDeclaration declaration) =>
        declaration is CodeTypeDefinition type
            ? type.Symbol.DeclaringSyntaxReferences.Any(reference =>
                DeclarationSyntax.HasDocumentation(
                    reference.GetSyntax(type.Source.Project.CancellationToken)
                )
            )
            : DeclarationSyntax.Owner(declaration) is { } syntax
                && DeclarationSyntax.HasDocumentation(syntax);

    /// <summary>The written modifier token, for reporting at it with <c>ReportAt</c>; <c>default</c> when this declaration's own syntax does not contain it. A partial type definition searches the part at its location.</summary>
    public static SyntaxToken ExplicitModifier(
        this ICodeDeclaration declaration,
        Modifier modifier
    ) =>
        DeclarationSyntax.Owner(declaration) is { } syntax
            ? DeclarationSyntax
                .Modifiers(syntax)
                .FirstOrDefault(token => token.IsKind((SyntaxKind)modifier))
            : default;

    /// <summary>Selects declarations with any of the exact ordinal names.</summary>
    public static CodeQuery<T> Named<T>(this CodeQuery<T> declarations, params string[] names)
        where T : ICodeDeclaration
    {
        foreach (var name in names)
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var selected = names.ToHashSet();
        return declarations.Where(declaration => selected.Contains(declaration.Name));
    }

    /// <summary>Selects declarations whose complete name matches a case-sensitive glob such as <c>*Async</c>.</summary>
    public static CodeQuery<T> NameMatching<T>(this CodeQuery<T> declarations, string pattern)
        where T : ICodeDeclaration
    {
        var match = new PathPattern(pattern);
        return declarations.Where(declaration => match.Matches(declaration.Name));
    }

    /// <summary>Selects declarations whose name contains any complete configured word; comparison defaults to ordinal.</summary>
    public static CodeQuery<T> WithNameContainingAnyWord<T>(
        this CodeQuery<T> declarations,
        IReadOnlyList<string> words,
        StringComparison comparison = StringComparison.Ordinal
    )
        where T : ICodeDeclaration
    {
        var selected = words.ToArray();
        foreach (var word in selected)
            ArgumentException.ThrowIfNullOrWhiteSpace(word);
        return declarations.Where(declaration =>
            declaration.NameWords.Any(word => selected.Any(value => word.Equals(value, comparison)))
        );
    }

    /// <summary>Selects declarations bearing any configured attribute, including derived attribute classes.</summary>
    public static CodeQuery<T> WithAttribute<T>(
        this CodeQuery<T> declarations,
        params CodeType[] attributes
    )
        where T : ICodeDeclaration
    {
        var selected = attributes.ToArray();
        return declarations.Where(declaration =>
            selected.Any(attribute => declaration.HasAttribute(attribute))
        );
    }

    /// <summary>Selects declarations whose written syntax contains the modifier token, independently of implicit defaults.</summary>
    public static CodeQuery<T> WithExplicitModifier<T>(
        this CodeQuery<T> declarations,
        Modifier modifier
    )
        where T : ICodeDeclaration =>
        declarations.Where(declaration => declaration.HasExplicitModifier(modifier));

    /// <summary>Selects declarations with the compiler's declared accessibility, including implicit defaults.</summary>
    public static CodeQuery<T> WithAccessibility<T>(
        this CodeQuery<T> declarations,
        Accessibility accessibility
    )
        where T : ICodeDeclaration =>
        declarations.Where(declaration => declaration.Accessibility == accessibility);
}
