using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>Ordinal name predicates for compiler symbols, such as call targets and reached types. Declarations share the same predicates through <see cref="DeclarationQueries"/>.</summary>
public static class NameQueries
{
    /// <summary>Identifier words for compiler symbols, including members, parameters and locals; unnamed symbols have no words.</summary>
    public static IReadOnlyList<string> NameWords(this ISymbol symbol) =>
        CodeIdentifier.NamedWords(symbol.Name);

    /// <summary>Tests a complete identifier word, using ordinal comparison unless configured otherwise; unnamed symbols never match.</summary>
    public static bool NameContainsWord(
        this ISymbol symbol,
        string word,
        StringComparison comparison = StringComparison.Ordinal
    ) => CodeIdentifier.NamedContainsWord(symbol.Name, word, comparison);

    /// <summary>Tests an ordinal suffix on a compiler symbol's unqualified name.</summary>
    public static bool NameEndsWith(this ISymbol symbol, string suffix) =>
        symbol.Name.EndsWith(suffix, StringComparison.Ordinal);

    /// <summary>Tests an ordinal prefix on a compiler symbol's unqualified name.</summary>
    public static bool NameStartsWith(this ISymbol symbol, string prefix) =>
        symbol.Name.StartsWith(prefix, StringComparison.Ordinal);

    /// <summary>Tests a case-sensitive glob against the complete compiler symbol name.</summary>
    public static bool NameMatches(this ISymbol symbol, string pattern) =>
        new PathPattern(pattern).Matches(symbol.Name);

    /// <summary>Matches compiler namespace segments; * matches one segment and a trailing ** includes the namespace itself and all descendants.</summary>
    public static bool IsInNamespace(this ISymbol symbol, string pattern) =>
        new PathPattern(pattern.Replace(".", "/") + "/").Matches(
            (
                symbol.ContainingNamespace?.IsGlobalNamespace == false
                    ? symbol.ContainingNamespace.ToDisplayString().Replace(".", "/")
                    : ""
            ) + "/"
        );
}
