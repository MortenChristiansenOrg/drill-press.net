using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>Ordinal name predicates for source declarations and compiler symbols.</summary>
public static class NameQueries
{
    /// <summary>Identifier words for any named compiler symbol, including members, parameters and locals.</summary>
    public static IReadOnlyList<string> NameWords(this ISymbol symbol) =>
        CodeIdentifier.Words(symbol.Name);

    /// <summary>Tests a complete identifier word, using ordinal comparison unless configured otherwise.</summary>
    public static bool NameContainsWord(
        this ISymbol symbol,
        string word,
        StringComparison comparison = StringComparison.Ordinal
    ) => CodeIdentifier.ContainsWord(symbol.Name, word, comparison);

    /// <summary>Tests a complete declared type-name word, using ordinal comparison unless configured otherwise.</summary>
    public static bool NameContainsWord(
        this CodeDeclaration type,
        string word,
        StringComparison comparison = StringComparison.Ordinal
    ) => CodeIdentifier.ContainsWord(type.Name, word, comparison);

    /// <summary>Tests a complete written type-name word, using ordinal comparison unless configured otherwise.</summary>
    public static bool NameContainsWord(
        this CodeTypeDeclaration type,
        string word,
        StringComparison comparison = StringComparison.Ordinal
    ) => CodeIdentifier.ContainsWord(type.Name, word, comparison);

    /// <summary>Tests a complete method-name word, including unresolved declarations.</summary>
    public static bool NameContainsWord(
        this CodeMethod method,
        string word,
        StringComparison comparison = StringComparison.Ordinal
    ) => CodeIdentifier.ContainsWord(method.Name, word, comparison);

    /// <summary>Selects methods containing any complete configured word; comparison defaults to ordinal.</summary>
    public static CodeQuery<CodeMethod> WhereNameContainsAnyWord(
        this CodeQuery<CodeMethod> methods,
        IReadOnlyList<string> words,
        StringComparison comparison = StringComparison.Ordinal
    ) => WithWords(methods, method => method.Name, words, comparison);

    /// <summary>Selects named type definitions containing any complete configured word; comparison defaults to ordinal.</summary>
    public static CodeQuery<CodeDeclaration> WhereNameContainsAnyWord(
        this CodeQuery<CodeDeclaration> types,
        IReadOnlyList<string> words,
        StringComparison comparison = StringComparison.Ordinal
    ) => WithWords(types, type => type.Name, words, comparison);

    /// <summary>Selects written type parts containing any complete configured word; comparison defaults to ordinal.</summary>
    public static CodeQuery<CodeTypeDeclaration> WhereNameContainsAnyWord(
        this CodeQuery<CodeTypeDeclaration> types,
        IReadOnlyList<string> words,
        StringComparison comparison = StringComparison.Ordinal
    ) => WithWords(types, type => type.Name, words, comparison);

    private static CodeQuery<T> WithWords<T>(
        CodeQuery<T> query,
        Func<T, string> name,
        IReadOnlyList<string> words,
        StringComparison comparison
    )
    {
        var selected = words.ToArray();
        foreach (var word in selected)
            ArgumentException.ThrowIfNullOrWhiteSpace(word);
        return query.Where(candidate =>
            CodeIdentifier
                .Words(name(candidate))
                .Any(word => selected.Any(value => word.Equals(value, comparison)))
        );
    }

    /// <summary>Tests an ordinal suffix on a source type name.</summary>
    public static bool NameEndsWith(this CodeDeclaration type, string suffix) =>
        type.Name.EndsWith(suffix, StringComparison.Ordinal);

    /// <summary>Tests an ordinal prefix on a source type name.</summary>
    public static bool NameStartsWith(this CodeDeclaration type, string prefix) =>
        type.Name.StartsWith(prefix, StringComparison.Ordinal);

    /// <summary>Tests a case-sensitive glob against the complete source type name.</summary>
    public static bool NameMatches(this CodeDeclaration type, string pattern) =>
        new PathPattern(pattern).Matches(type.Name);

    /// <summary>Tests an ordinal suffix on a method's written name, including unresolved methods.</summary>
    public static bool NameEndsWith(this CodeMethod method, string suffix) =>
        method.Name.EndsWith(suffix, StringComparison.Ordinal);

    /// <summary>Tests an ordinal prefix on a method's written name.</summary>
    public static bool NameStartsWith(this CodeMethod method, string prefix) =>
        method.Name.StartsWith(prefix, StringComparison.Ordinal);

    /// <summary>Tests a case-sensitive glob against the complete method name.</summary>
    public static bool NameMatches(this CodeMethod method, string pattern) =>
        new PathPattern(pattern).Matches(method.Name);

    /// <summary>Tests an ordinal suffix on a compiler symbol's unqualified name.</summary>
    public static bool NameEndsWith(this ISymbol symbol, string suffix) =>
        symbol.Name.EndsWith(suffix, StringComparison.Ordinal);

    /// <summary>Tests an ordinal prefix on a compiler symbol's unqualified name.</summary>
    public static bool NameStartsWith(this ISymbol symbol, string prefix) =>
        symbol.Name.StartsWith(prefix, StringComparison.Ordinal);

    /// <summary>Tests a case-sensitive glob against the complete compiler symbol name.</summary>
    public static bool NameMatches(this ISymbol symbol, string pattern) =>
        new PathPattern(pattern).Matches(symbol.Name);

    /// <summary>Matches compiler namespace segments, including the root for trailing **.</summary>
    public static bool IsInNamespace(this ISymbol symbol, string pattern) =>
        new PathPattern(pattern.Replace('.', '/') + "/").Matches(
            (
                symbol.ContainingNamespace?.IsGlobalNamespace == false
                    ? symbol.ContainingNamespace.ToDisplayString().Replace('.', '/')
                    : ""
            ) + "/"
        );
}
