using DrillPress.Operations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress.Semantics;

/// <summary>A source-rooted, symbol-bound member path with conservative structural comparison.</summary>
public sealed class BoundMemberPath
{
    internal BoundMemberPath(CodeExpression root, IReadOnlyList<MemberPathStep> steps)
    {
        Root = root;
        Steps = steps;
    }

    /// <summary>The explicit model root; equality means structural storage correspondence, not time-invariant runtime identity.</summary>
    public CodeExpression Root { get; }

    /// <summary>Bound members and typed constant indices in access order.</summary>
    public IReadOnlyList<MemberPathStep> Steps { get; }

    /// <summary>Reads property/field/constant-index chains rooted in a local, parameter or this. Dynamic, conversions and variable indices return null.</summary>
    public static BoundMemberPath? FromExpression(CodeExpression expression)
    {
        if (!expression.IsResolved)
            return null;
        var steps = new List<MemberPathStep>();
        var operation = expression.Operation;
        while (operation is not null)
        {
            switch (operation)
            {
                case IConversionOperation { Conversion.IsIdentity: true } conversion:
                    operation = conversion.Operand;
                    continue;
                case IPropertyReferenceOperation { Instance: { } receiver } property:
                    var indices = ReadIndices(
                        property
                            .Arguments.OrderBy(argument => argument.Parameter?.Ordinal)
                            .Select(argument => argument.Value)
                    );
                    if (indices is null)
                        return null;
                    steps.Add(new(property.Property, indices, property.Property.Type));
                    operation = receiver;
                    continue;
                case IFieldReferenceOperation { Instance: { } receiver } field:
                    steps.Add(new(field.Field, [], field.Field.Type));
                    operation = receiver;
                    continue;
                case IArrayElementReferenceOperation array:
                    var arrayIndices = ReadIndices(array.Indices);
                    if (arrayIndices is null || array.Type is null)
                        return null;
                    steps.Add(new(null, arrayIndices, array.Type));
                    operation = array.ArrayReference;
                    continue;
                case ILocalReferenceOperation
                or IParameterReferenceOperation
                or IInstanceReferenceOperation:
                    steps.Reverse();
                    var root =
                        operation.IsImplicit
                            ? CodeExpression.ImplicitReceiver(
                                expression.Source,
                                expression.Syntax,
                                operation
                            )
                        : operation.Syntax is ExpressionSyntax syntax
                            ? new CodeExpression(expression.Source, syntax)
                        : null;
                    return root is null ? null : new(root, steps.AsReadOnly());
                default:
                    return null;
            }
        }
        return null;
    }

    /// <summary>Compares a checked access to this path; a delegate may supply additional root/alias evidence.</summary>
    public PathCorrelation CompareTo(
        CodeExpression expression,
        Func<CodeExpression, CodeExpression, PathCorrelation>? rootRelation = null
    ) =>
        FromExpression(expression) is { } other
            ? CompareTo(other, rootRelation)
            : PathCorrelation.Unknown;

    /// <summary>Compares contextual root, substituted member identities and typed constant indices.</summary>
    public PathCorrelation CompareTo(
        BoundMemberPath other,
        Func<CodeExpression, CodeExpression, PathCorrelation>? rootRelation = null
    )
    {
        if (Root.Source.Project != other.Root.Source.Project)
            return PathCorrelation.Unknown;
        var roots = (rootRelation ?? SameRoot)(Root, other.Root);
        if (roots != PathCorrelation.Match)
            return roots;
        if (Steps.Count != other.Steps.Count)
            return PathCorrelation.Different;
        for (var index = 0; index < Steps.Count; index++)
        {
            var first = Steps[index];
            var second = other.Steps[index];
            if (
                !SymbolEqualityComparer.Default.Equals(first.Member, second.Member)
                || !SymbolEqualityComparer.Default.Equals(first.Type, second.Type)
                || first.Indices.Count != second.Indices.Count
            )
                return PathCorrelation.Different;
            for (var item = 0; item < first.Indices.Count; item++)
                if (
                    !SymbolEqualityComparer.Default.Equals(
                        first.Indices[item].Type,
                        second.Indices[item].Type
                    ) || !Equals(first.Indices[item].Value, second.Indices[item].Value)
                )
                    return PathCorrelation.Different;
        }
        return PathCorrelation.Match;
    }

    /// <summary>Compares local/parameter storage or this type in one compilation. Different variables can still alias at runtime.</summary>
    public static PathCorrelation SameRoot(CodeExpression first, CodeExpression second)
    {
        if (first.Source.Project != second.Source.Project)
            return PathCorrelation.Unknown;
        var left = first.Operation;
        var right = second.Operation;
        if (left is IInstanceReferenceOperation && right is IInstanceReferenceOperation)
            return SymbolEqualityComparer.Default.Equals(left.Type, right.Type)
                ? PathCorrelation.Match
                : PathCorrelation.Different;
        var leftSymbol = left is null ? null : CodeExpression.ReferencedSymbol(left);
        var rightSymbol = right is null ? null : CodeExpression.ReferencedSymbol(right);
        return
            leftSymbol is ILocalSymbol or IParameterSymbol
            && rightSymbol is ILocalSymbol or IParameterSymbol
            ? SymbolEqualityComparer.Default.Equals(leftSymbol, rightSymbol)
                ? PathCorrelation.Match
                : PathCorrelation.Different
            : PathCorrelation.Unknown;
    }

    private static IReadOnlyList<PathConstant>? ReadIndices(IEnumerable<IOperation> operations)
    {
        var values = new List<PathConstant>();
        foreach (var operation in operations)
        {
            if (
                operation.Type is not { TypeKind: not TypeKind.Error } type
                || operation.ConstantValue is not { HasValue: true } constant
            )
                return null;
            values.Add(new(type, constant.Value));
        }
        return values.AsReadOnly();
    }
}
