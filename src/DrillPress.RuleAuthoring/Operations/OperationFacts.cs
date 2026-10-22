using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

/// <summary>Small compiler-operation projections with explicit unwrapping and scope boundaries.</summary>
public static class OperationFacts
{
    /// <summary>Removes conversion operations. Explicit conversions are retained unless requested; parenthesized operations are retained.</summary>
    [return: NotNullIfNotNull(nameof(operation))]
    public static IOperation? WithoutConversions(
        this IOperation? operation,
        bool includeExplicit = false
    )
    {
        while (
            operation is IConversionOperation conversion
            && (includeExplicit || conversion.IsImplicit)
        )
            operation = conversion.Operand;
        return operation;
    }

    /// <summary>The instance or first extension argument, with implicit conversions removed.</summary>
    public static IOperation? ReceiverValue(this IInvocationOperation call) =>
        (
            call.TargetMethod.IsExtensionMethod && call.Arguments.Length > 0
                ? call.Arguments[0].Value
                : call.Instance
        ).WithoutConversions();

    /// <summary>The outermost operation containing this operation, including nested function bodies.</summary>
    public static IOperation RootOperation(this IOperation operation)
    {
        while (operation.Parent is { } parent)
            operation = parent;
        return operation;
    }

    /// <summary>The referenced local or parameter; does not follow members or conversions.</summary>
    public static ISymbol? ReferencedVariable(this IOperation? operation) =>
        operation switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ => null,
        };

    /// <summary>A local or field's single written initializer in the current syntax tree; never infers later assignments.</summary>
    public static CodeExpression? Initializer(this ISymbol symbol, AnalysisSource source) =>
        symbol.DeclaringSyntaxReferences is [var reference]
        && reference.SyntaxTree == source.Tree
        && reference.GetSyntax(source.Project.CancellationToken)
            is VariableDeclaratorSyntax { Initializer.Value: var value }
            ? new(source, value)
            : null;
}
