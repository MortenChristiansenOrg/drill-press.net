using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>
/// Recognizes framework comparisons and formatting whose result depends on the current culture. Integer signs alone and
/// invariant date formats don't count. This is a bounded classification of common framework APIs, not a complete
/// globalization audit.
/// </summary>
public static class CultureQueries
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

    private static readonly CodeType _numberStyles = CodeType.Of<NumberStyles>();

    private static readonly CodeType[] _temporalTypes =
    [
        CodeType.Of<DateTime>(),
        CodeType.Of<DateTimeOffset>(),
        CodeType.Of<DateOnly>(),
        CodeType.Of<TimeOnly>(),
        CodeType.Of<TimeSpan>(),
    ];

    private static readonly CodeType[] _providers =
    [
        CodeType.Of<IFormatProvider>(),
        CodeType.Of<CultureInfo>(),
    ];

    private static readonly CodeType[] _comparisons =
    [
        CodeType.Of<StringComparison>(),
        CodeType.Of<CompareOptions>(),
        CodeType.Framework("System.Collections.Generic.IComparer<>"),
        CodeType.Framework("System.Comparison<>"),
    ];

    private static readonly CodeType _half = CodeType.Of<Half>();
    private static readonly CodeType _enumerable = CodeType.Framework("System.Linq.Enumerable");
    private static readonly CodeType _stringList = CodeType.Of<List<string>>();
    private static readonly CodeType _array = CodeType.Of<Array>();
    private static readonly CodeType _stringBuilder = CodeType.Of<StringBuilder>();
    private static readonly CodeType _convert = CodeType.Framework("System.Convert");

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

    /// <summary>Whether the call compares or changes the case of strings or characters, or sorts strings, with the current culture because it passes no comparison, comparer or format provider.</summary>
    public static bool ComparesWithCulture(this CodeInvocation call)
    {
        var target = call.Target;
        if (target.Parameters.Any(parameter => IsComparison(parameter.Type)))
            return false;
        var owner = target.ContainingType;
        if (owner.SpecialType == SpecialType.System_String)
            return target.Name switch
            {
                // The char overloads compare ordinally.
                _ when _culturePrefixMethods.Contains(target.Name) => target.Parameters
                    is [{ Type.SpecialType: SpecialType.System_String }, ..],
                "Compare" or "CompareTo" => true,
                "ToLower" or "ToUpper" => target.Parameters.Length == 0,
                _ => false,
            };
        if (owner.SpecialType == SpecialType.System_Char)
            return target.Name is "ToLower" or "ToUpper" && target.Parameters.Length == 1;
        // Sorting strings without a comparer uses the current culture.
        if (_enumerable.Matches(owner) && _sortingMethods.Contains(target.Name))
        {
            var key = target.Name is "Order" or "OrderDescending"
                ? target.TypeArguments[0]
                : target.TypeArguments[^1];
            return key.SpecialType == SpecialType.System_String;
        }
        if (target.Name == "Sort" && target.Parameters.Length <= 1)
            return _stringList.Matches(owner)
                || _array.Matches(owner)
                    && target.Parameters
                        is [
                            {
                                Type: IArrayTypeSymbol
                                {
                                    ElementType.SpecialType: SpecialType.System_String
                                }
                            },
                        ];
        return false;
    }

    /// <summary>Whether the call formats or parses a number or date, or formats a composite string, with the current culture because it passes no format provider.</summary>
    public static bool FormatsWithCulture(this CodeInvocation call)
    {
        var target = call.Target;
        if (HasProviderParameter(target))
            return false;
        var owner = target.ContainingType;
        switch (target.Name)
        {
            case "ToString" when target.Parameters.Length == 0:
                // An integer's default format only depends on the culture's minus sign.
                return IsFractional(owner) || IsTemporal(owner);
            case "ToString"
                when IsTemporal(owner)
                    && IsInvariantDateFormat(call.Argument("format")?.Value?.TextValue):
                return false;
            // Parsing an integer only depends on the culture's signs, like its default format, unless its number
            // styles allow group separators, a decimal point or a currency symbol.
            case "Parse" or "TryParse" when IsIntegral(owner) && !AllowsCultureSymbols(call):
                return false;
            case "ToString" or "Parse" or "TryParse" or "ParseExact" or "TryParseExact":
                return IsFormattedWithCulture(owner) && HasProviderOverload(target);
            case "Format" when owner.SpecialType == SpecialType.System_String:
                return true;
            case "AppendFormat" when _stringBuilder.Matches(owner):
                return true;
            default:
                return _convert.Matches(owner)
                    && target.Parameters is [{ Type: var input }]
                    && (
                        input.SpecialType == SpecialType.System_String
                        || IsFormattedWithCulture(input)
                    )
                    && HasProviderOverload(target);
        }
    }

    /// <summary>Whether an interpolation hole formats its value with the current culture: a fraction, a date or time without an invariant format, or an integer with a format that groups digits.</summary>
    public static bool FormatsWithCulture(this CodeNode<InterpolationSyntax> hole)
    {
        var type = hole
            .Source.Model.GetTypeInfo(hole.Syntax.Expression, hole.Source.Project.CancellationToken)
            .Type;
        var format = hole.Syntax.FormatClause?.FormatStringToken.ValueText;
        if (IsFractional(type))
            return true;
        if (IsTemporal(type))
            return !IsInvariantDateFormat(format);
        // An integer without a format only depends on the culture's minus sign; group separators come from formats
        // such as N0. Hexadecimal and zero-padded digit formats (D2, 00) are the same everywhere.
        return IsIntegral(type)
            && format is not null
            && !(format.Length > 0 && format[0] is 'D' or 'd' or 'X' or 'x')
            && !format.All(character => character == '0');
    }

    private static bool AllowsCultureSymbols(CodeInvocation call) =>
        call.Operation.Arguments.FirstOrDefault(argument =>
            argument.Parameter is { } parameter && _numberStyles.Matches(parameter.Type)
        )
            is { } style
        && (
            style.Value.ConstantValue is not { HasValue: true, Value: int value }
            || (value & (int)CultureSymbolStyles) != 0
        );

    private const NumberStyles CultureSymbolStyles =
        NumberStyles.AllowThousands
        | NumberStyles.AllowDecimalPoint
        | NumberStyles.AllowCurrencySymbol;

    private static bool IsFormattedWithCulture(ITypeSymbol? type) =>
        IsIntegral(type) || IsFractional(type) || IsTemporal(type);

    private static bool IsIntegral(ITypeSymbol? type) =>
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
    private static bool IsFractional(ITypeSymbol? type) =>
        Underlying(type) is { } value
        && (
            value.SpecialType
                is SpecialType.System_Single
                    or SpecialType.System_Double
                    or SpecialType.System_Decimal
            || _half.Matches(value)
        );

    private static bool IsTemporal(ITypeSymbol? type) =>
        Underlying(type) is { } value && _temporalTypes.Any(temporal => temporal.Matches(value));

    private static bool IsInvariantDateFormat(string? format) =>
        format is not null && _invariantDateFormats.Contains(format);

    private static bool HasProviderParameter(IMethodSymbol method) =>
        method.Parameters.Any(parameter => IsProvider(parameter.Type));

    // The same type declares an overload with the same name that adds a format provider.
    private static bool HasProviderOverload(IMethodSymbol method) =>
        method
            .ContainingType.GetMembers(method.Name)
            .OfType<IMethodSymbol>()
            .Any(candidate =>
                HasProviderParameter(candidate) && candidate.IsStatic == method.IsStatic
            );

    private static bool IsProvider(ITypeSymbol type) =>
        _providers.Any(provider => provider.Matches(type));

    private static bool IsComparison(ITypeSymbol type) =>
        IsProvider(type) || _comparisons.Any(comparison => comparison.Matches(type));

    private static ITypeSymbol? Underlying(ITypeSymbol? type) =>
        type
            is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
            } nullable
            ? nullable.TypeArguments[0]
            : type;
}
