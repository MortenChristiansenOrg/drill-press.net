using System.Globalization;
using DrillPress;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

internal sealed record ExpressionShape(string Key, CodeExpression? Capture)
{
    internal static ExpressionShape? Read(
        CodeExpression expression,
        OneHoleTemplateOptions? options
    )
    {
        if (!expression.IsResolved)
            return null;
        if (options is null)
            return
                expression.Constant is { HasValue: true } constant
                && (expression.Type ?? expression.TypeInfo.ConvertedType)
                    is { TypeKind: not TypeKind.Error } type
                ? new(ConstantKey(type, constant.Value), null)
                : null;
        if (
            expression.Type?.SpecialType != SpecialType.System_String
            || expression.TypeInfo.ConvertedType?.SpecialType != SpecialType.System_String
            || expression.Syntax.DescendantNodesAndSelf().Skip(options.MaximumNodes).Any()
            || !(
                expression.Syntax is InterpolatedStringExpressionSyntax
                    && options.Shapes.HasFlag(TemplateShapes.Interpolation)
                || expression.Syntax is BinaryExpressionSyntax
                    && options.Shapes.HasFlag(TemplateShapes.Concatenation)
            )
        )
            return null;
        var shape = Part(expression, options);
        return shape?.Capture is null ? null : shape;
    }

    private static ExpressionShape? Part(CodeExpression expression, OneHoleTemplateOptions options)
    {
        expression.Source.Project.CancellationToken.ThrowIfCancellationRequested();
        var model = expression.Source.Model;
        var syntax = expression.Syntax;
        if (syntax is ParenthesizedExpressionSyntax parenthesized)
            return Part(new(expression.Source, parenthesized.Expression), options);
        if (
            expression.Constant is { HasValue: true } constant
            && expression.Type is { } constantType
        )
            return new(ConstantKey(constantType, constant.Value), null);
        var operation = expression.Operation;
        if (
            operation
            is ILocalReferenceOperation { Local.RefKind: RefKind.None }
                or IParameterReferenceOperation { Parameter.RefKind: RefKind.None }
        )
            return options.AllowedCapture(expression)
                ? new("hole" + TypeKey(expression), expression)
                : null;
        if (
            syntax is BinaryExpressionSyntax binary
            && options.Shapes.HasFlag(TemplateShapes.Concatenation)
            && operation
                is IBinaryOperation
                {
                    OperatorKind: BinaryOperatorKind.Add,
                    OperatorMethod: null,
                    Type.SpecialType: SpecialType.System_String
                }
        )
            return Combine(
                "concat" + TypeKey(expression),
                [
                    Part(new(expression.Source, binary.Left), options),
                    Part(new(expression.Source, binary.Right), options),
                ]
            );
        if (
            syntax is InterpolatedStringExpressionSyntax interpolation
            && options.Shapes.HasFlag(TemplateShapes.Interpolation)
            && operation is IInterpolatedStringOperation
            && interpolation.Contents.OfType<InterpolationSyntax>().Count() == 1
        )
        {
            var parts = new List<ExpressionShape?>();
            foreach (var content in interpolation.Contents)
            {
                if (content is InterpolatedStringTextSyntax text)
                    parts.Add(new("text" + Encode(text.TextToken.ValueText), null));
                else if (content is InterpolationSyntax hole)
                {
                    var alignment =
                        hole.AlignmentClause is null ? "none"
                        : model.GetConstantValue(hole.AlignmentClause.Value)
                            is { HasValue: true, Value: int value }
                            ? value.ToString(CultureInfo.InvariantCulture)
                        : null;
                    if (alignment is null)
                        return null;
                    parts.Add(
                        Combine(
                            "interpolation"
                                + Encode(alignment)
                                + Encode(hole.FormatClause?.FormatStringToken.ValueText ?? ""),
                            [Part(new(expression.Source, hole.Expression), options)]
                        )
                    );
                }
            }
            return Combine("string" + TypeKey(expression), parts);
        }
        if (
            operation
                is IInvocationOperation
                {
                    Instance: null,
                    TargetMethod.IsStatic: true,
                    TargetMethod.MethodKind: MethodKind.Ordinary
                } call
            && options.AllowedCalls.Contains(call.TargetMethod)
            && call.Arguments.All(argument =>
                argument.ArgumentKind == ArgumentKind.Explicit
                && argument.Parameter is { RefKind: RefKind.None, IsParams: false }
            )
        )
        {
            var parts = call
                .Arguments.Select(argument =>
                    argument.Value.Syntax is ExpressionSyntax value
                        ? Combine(
                            "parameter" + argument.Parameter!.Ordinal,
                            [Part(new(expression.Source, value), options)]
                        )
                        : null
                )
                .ToArray();
            var result = Combine(
                "call" + Encode(RewriteSymbols.Identity(call.TargetMethod)!) + TypeKey(expression),
                parts
            );
            return result?.Capture is null ? null : result;
        }
        return null;
    }

    private static ExpressionShape? Combine(string kind, IEnumerable<ExpressionShape?> values)
    {
        var parts = values.ToArray();
        if (parts.Any(part => part is null) || parts.Count(part => part!.Capture is not null) > 1)
            return null;
        return new(
            kind + string.Concat(parts.Select(part => Encode(part!.Key))),
            parts.Select(part => part!.Capture).FirstOrDefault(capture => capture is not null)
        );
    }

    private static string TypeKey(CodeExpression expression)
    {
        var conversion = expression.Conversion;
        return TemplateType(expression.Type)
            + TemplateType(expression.TypeInfo.ConvertedType)
            + $"{expression.TypeInfo.Nullability.Annotation}:{expression.TypeInfo.Nullability.FlowState}:{expression.TypeInfo.ConvertedNullability.Annotation}:{expression.TypeInfo.ConvertedNullability.FlowState}:"
            + $"{conversion.IsIdentity}:{conversion.IsImplicit}:{conversion.IsNumeric}:{conversion.IsReference}:{conversion.IsBoxing}:{conversion.IsUnboxing}:{conversion.IsUserDefined}:{conversion.IsEnumeration}:{conversion.IsDynamic}:{conversion.IsConstantExpression}:{conversion.IsNullable}:"
            + Encode(RewriteSymbols.Identity(conversion.MethodSymbol) ?? "");
    }

    private static string TemplateType(ITypeSymbol? type) =>
        Encode(RewriteSymbols.Identity(type) ?? "")
        + Encode(
            type?.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
                    SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                        | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                )
            )
                ?? ""
        );

    internal static string ConstantKey(ITypeSymbol type, object? value) =>
        "constant"
        + Encode(RewriteSymbols.Identity(type)!)
        + (value is null ? "0" : "1")
        + Encode(
            value switch
            {
                null => "null",
                float number => BitConverter
                    .SingleToInt32Bits(number)
                    .ToString(CultureInfo.InvariantCulture),
                double number => BitConverter
                    .DoubleToInt64Bits(number)
                    .ToString(CultureInfo.InvariantCulture),
                IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString()!,
            }
        );

    private static string Encode(string value) =>
        value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
}
