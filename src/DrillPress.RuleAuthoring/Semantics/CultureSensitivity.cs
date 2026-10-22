using DrillPress;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>
/// Classifies common framework comparisons and formatting that depend on the current culture.
/// Integer signs alone are ignored; invariant date formats are excluded. This is a bounded classification, not a complete globalization audit.
/// </summary>
public static class CultureSensitivity
{
    // Formats whose output is the same in every culture: round-trip, RFC 1123, sortable and universal dates, and the
    // constant TimeSpan format.
    private static readonly HashSet<string> _invariantDateFormats =
    [
        "O",
        "o",
        "R",
        "r",
        "s",
        "u",
        "c",
    ];

    private static readonly HashSet<string> _temporalTypes =
    [
        "System.DateTimeOffset",
        "System.DateOnly",
        "System.TimeOnly",
        "System.TimeSpan",
    ];

    /// <summary>Whether a value of this type, or the value of this nullable, is formatted and parsed with a culture.</summary>
    public static bool IsFormattedWithCulture(ITypeSymbol? type) =>
        IsIntegral(type) || IsFractional(type) || IsTemporal(type);

    /// <summary>Recognizes the eight built-in integral types, unwrapping nullable values.</summary>
    public static bool IsIntegral(ITypeSymbol? type) =>
        Underlying(type)?.SpecialType
            is SpecialType.System_SByte
                or SpecialType.System_Byte
                or SpecialType.System_Int16
                or SpecialType.System_UInt16
                or SpecialType.System_Int32
                or SpecialType.System_UInt32
                or SpecialType.System_Int64
                or SpecialType.System_UInt64;

    // Decimal separators differ between cultures (12,5 in Danish).
    /// <summary>Recognizes float, double, decimal and Half, unwrapping nullable values.</summary>
    public static bool IsFractional(ITypeSymbol? type) =>
        Underlying(type) is { } value
        && (
            value.SpecialType
                is SpecialType.System_Single
                    or SpecialType.System_Double
                    or SpecialType.System_Decimal
            || value.ToDisplayString() is "System.Half"
        );

    /// <summary>Whether a constant date format gives the same text in every culture.</summary>
    public static bool IsInvariantDateFormat(string? format) =>
        format is not null && _invariantDateFormats.Contains(format);

    /// <summary>Recognizes DateTime, DateTimeOffset, DateOnly, TimeOnly and TimeSpan, unwrapping nullable values.</summary>
    public static bool IsTemporal(ITypeSymbol? type) =>
        Underlying(type) is { } value
        && (
            value.SpecialType == SpecialType.System_DateTime
            || _temporalTypes.Contains(value.ToDisplayString())
        );

    /// <summary>Whether an interpolation hole formats its value with the current culture.</summary>
    public static bool FormatsWithCulture(InterpolationSyntax hole, ITypeSymbol? type)
    {
        var format = hole.FormatClause?.FormatStringToken.ValueText;
        if (IsFractional(type))
        {
            return true;
        }
        if (IsTemporal(type))
        {
            return !IsInvariantDateFormat(format);
        }
        // An integer without a format only depends on the culture's minus sign; group separators come from formats
        // such as N0. Hexadecimal and zero-padded digit formats (D2, 00) are the same everywhere.
        return IsIntegral(type)
            && format is not null
            && !(format.Length > 0 && format[0] is 'D' or 'd' or 'X' or 'x')
            && !format.All(character => character == '0');
    }

    /// <summary>Whether a method takes IFormatProvider or CultureInfo.</summary>
    public static bool HasProviderParameter(IMethodSymbol method) =>
        method.Parameters.Any(parameter => IsProvider(parameter.Type));

    /// <summary>Whether a method takes a format provider, StringComparison, CompareOptions, IComparer or Comparison.</summary>
    public static bool HasComparisonParameter(IMethodSymbol method) =>
        method.Parameters.Any(parameter =>
            IsProvider(parameter.Type)
            || MetadataName(parameter.Type)
                is "System.StringComparison"
                    or "System.Globalization.CompareOptions"
                    or "System.Collections.Generic.IComparer`1"
                    or "System.Comparison`1"
        );

    /// <summary>Whether the same type declares an overload with the same name that adds a format provider.</summary>
    public static bool HasProviderOverload(IMethodSymbol method) =>
        method
            .ContainingType.GetMembers(method.Name)
            .OfType<IMethodSymbol>()
            .Any(candidate =>
                HasProviderParameter(candidate) && candidate.IsStatic == method.IsStatic
            );

    private static bool IsProvider(ITypeSymbol type) =>
        MetadataName(type) is "System.IFormatProvider" or "System.Globalization.CultureInfo";

    /// <summary>The type's namespace-qualified metadata name, such as <c>System.Collections.Generic.IComparer`1</c>, regardless of nullability and type arguments.</summary>
    public static string MetadataName(ITypeSymbol type) =>
        type.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? $"{ns.ToDisplayString()}.{type.MetadataName}"
            : type.MetadataName;

    private static ITypeSymbol? Underlying(ITypeSymbol? type) =>
        type
            is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
            } nullable
            ? nullable.TypeArguments[0]
            : type;

    /// <summary>Whether this call uses one of the framework's culture-dependent comparisons without a comparison argument.</summary>
    public static bool ComparesWithCulture(CodeInvocation call) => ComparesWithCulture(call.Target);

    private static readonly HashSet<string> _culturePrefixMethods =
    [
        "StartsWith",
        "EndsWith",
        "IndexOf",
        "LastIndexOf",
    ];

    private static readonly HashSet<string> _sortingMethods =
    [
        "Order",
        "OrderDescending",
        "OrderBy",
        "OrderByDescending",
        "ThenBy",
        "ThenByDescending",
    ];

    /// <summary>Classifies string and character comparison/casing and string sorting without an explicit comparer.</summary>
    public static bool ComparesWithCulture(IMethodSymbol target)
    {
        if (CultureSensitivity.HasComparisonParameter(target))
        {
            return false;
        }
        var owner = target.ContainingType;
        if (owner.SpecialType == SpecialType.System_String)
        {
            return target.Name switch
            {
                // The char overloads compare ordinally.
                _ when _culturePrefixMethods.Contains(target.Name) => target.Parameters
                    is [{ Type.SpecialType: SpecialType.System_String }, ..],
                "Compare" or "CompareTo" => true,
                "ToLower" or "ToUpper" => target.Parameters.Length == 0,
                _ => false,
            };
        }
        if (owner.SpecialType == SpecialType.System_Char)
        {
            return target.Name is "ToLower" or "ToUpper" && target.Parameters.Length == 1;
        }
        // Sorting strings without a comparer uses the current culture.
        var definition = owner.ToDisplayString();
        if (definition is "System.Linq.Enumerable" && _sortingMethods.Contains(target.Name))
        {
            var key = target.Name is "Order" or "OrderDescending"
                ? target.TypeArguments[0]
                : target.TypeArguments[^1];
            return key.SpecialType == SpecialType.System_String;
        }
        if (target.Name == "Sort" && target.Parameters.Length <= 1)
        {
            return definition == "System.Collections.Generic.List<string>"
                || definition == "System.Array"
                    && target.Parameters
                        is [
                            {
                                Type: IArrayTypeSymbol
                                {
                                    ElementType.SpecialType: SpecialType.System_String
                                }
                            },
                        ];
        }
        return false;
    }

    /// <summary>Classifies numeric/date parsing, formatting and composite formatting without an explicit provider, excluding integer signs alone and invariant date formats.</summary>
    public static bool FormatsWithCulture(CodeInvocation call)
    {
        var target = call.Target;
        if (CultureSensitivity.HasProviderParameter(target))
        {
            return false;
        }
        var owner = target.ContainingType;
        switch (target.Name)
        {
            case "ToString" when target.Parameters.Length == 0:
                // An integer's default format only depends on the culture's minus sign.
                return CultureSensitivity.IsFractional(owner)
                    || CultureSensitivity.IsTemporal(owner);
            case "ToString"
                when CultureSensitivity.IsTemporal(owner)
                    && CultureSensitivity.IsInvariantDateFormat(
                        call.Argument("format")?.Value?.TextValue
                    ):
                return false;
            // Parsing an integer only depends on the culture's signs, like its default format.
            case "Parse" or "TryParse" when CultureSensitivity.IsIntegral(owner):
                return false;
            case "ToString" or "Parse" or "TryParse" or "ParseExact" or "TryParseExact":
                return CultureSensitivity.IsFormattedWithCulture(owner)
                    && CultureSensitivity.HasProviderOverload(target);
            case "Format" when owner.SpecialType == SpecialType.System_String:
                return true;
            case "AppendFormat" when owner.ToDisplayString() == "System.Text.StringBuilder":
                return true;
            default:
                return owner.ToDisplayString() == "System.Convert"
                    && target.Parameters is [{ Type: var input }]
                    && (
                        input.SpecialType == SpecialType.System_String
                        || CultureSensitivity.IsFormattedWithCulture(input)
                    )
                    && CultureSensitivity.HasProviderOverload(target);
        }
    }
}
