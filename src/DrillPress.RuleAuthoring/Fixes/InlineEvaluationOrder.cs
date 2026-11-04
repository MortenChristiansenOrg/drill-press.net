using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

// The evaluation an inlined initializer moves past within the read's statement, and whether that evaluation is
// pure enough for the move to be unobservable: it neither throws nor observes the initializer's effects.
internal static class InlineEvaluationOrder
{
    // The outermost operations of the header that run before the read: earlier operands and implicit receivers.
    internal static IReadOnlyList<IOperation> Before(InlineReadPosition position)
    {
        var ancestors = new HashSet<IOperation>();
        for (var operation = position.Read; operation is not null; operation = operation.Parent)
        {
            ancestors.Add(operation);
            if (operation == position.Header)
                break;
        }
        var before = new List<IOperation>();
        Collect(position.Header, position.Read.Syntax.SpanStart, ancestors, before);
        return before.AsReadOnly();
    }

    private static void Collect(
        IOperation operation,
        int read,
        HashSet<IOperation> ancestors,
        List<IOperation> before
    )
    {
        foreach (var child in operation.ChildOperations)
        {
            if (ancestors.Contains(child) || child.Syntax.SpanStart < read && IsWrapper(child))
                Collect(child, read, ancestors, before);
            else if (child.Syntax.SpanStart < read)
                before.Add(child);
        }
    }

    // Written syntax around an operand that evaluates nothing itself, such as an earlier declarator or argument.
    private static bool IsWrapper(IOperation operation) =>
        operation is IVariableDeclaratorOperation or IVariableInitializerOperation
        || operation is IArgumentOperation { IsImplicit: false };

    // The first operation that may throw or observe the initializer's effects, or null when all are pure.
    internal static IOperation? FirstImpure(
        AnalysisSource source,
        LocalDeclarationStatementSyntax declaration,
        ExpressionSyntax initializer,
        IReadOnlyList<IOperation> before
    )
    {
        var context = new Purity(source, declaration, initializer);
        return before.FirstOrDefault(operation => !context.IsPure(operation));
    }

    private sealed class Purity(
        AnalysisSource source,
        LocalDeclarationStatementSyntax declaration,
        ExpressionSyntax initializer
    )
    {
        private readonly Lazy<DataFlowAnalysis?> _initializerFlow = new(() =>
            source.Model.AnalyzeDataFlow(initializer) is { Succeeded: true } flow ? flow : null
        );

        private readonly Lazy<INamedTypeSymbol?> _containingType = new(() =>
            source
                .Model.GetEnclosingSymbol(declaration.SpanStart, source.Project.CancellationToken)
                ?.ContainingType
        );

        internal bool IsPure(IOperation operation) =>
            operation.ConstantValue.HasValue
            || operation switch
            {
                ILiteralOperation
                or IDefaultValueOperation
                or ITypeOfOperation
                or ISizeOfOperation
                or INameOfOperation => true,
                IInstanceReferenceOperation reference => IsImmutableThis(reference.Type),
                IParameterReferenceOperation parameter => parameter.Parameter.RefKind
                    == RefKind.None
                    && !IsPrimaryConstructorParameter(parameter.Parameter)
                    && IsStable(parameter.Parameter),
                ILocalReferenceOperation local => local.Local.RefKind == RefKind.None
                    && IsStable(local.Local),
                IFieldReferenceOperation field => IsReadOnlyField(field),
                IConversionOperation conversion => conversion.OperatorMethod is null
                    && IsSafeConversion(conversion.GetConversion())
                    && IsPure(conversion.Operand),
                IParenthesizedOperation parenthesized => IsPure(parenthesized.Operand),
                IArgumentOperation argument => IsPure(argument.Value),
                _ => false,
            };

        // A primary constructor parameter is state of the instance that any member may change.
        private static bool IsPrimaryConstructorParameter(IParameterSymbol parameter) =>
            parameter.ContainingSymbol
                is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
            && constructor.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax() is TypeDeclarationSyntax
            );

        // A struct's this is a variable that the initializer's calls may reassign or mutate.
        private static bool IsImmutableThis(ITypeSymbol? type) =>
            type is { IsReferenceType: true } or { IsValueType: true, IsReadOnly: true };

        private static bool IsSafeConversion(Conversion conversion) =>
            conversion.IsIdentity
            || conversion.IsImplicit
                && (
                    conversion.IsReference
                    || conversion.IsBoxing
                    || conversion.IsNumeric
                    || conversion.IsNullable
                );

        // Readonly fields change only in constructors and init accessors; static ones of other types may run
        // their type's initializer when read.
        private bool IsReadOnlyField(IFieldReferenceOperation reference)
        {
            var field = reference.Field;
            if (field.IsConst)
                return true;
            if (!field.IsReadOnly)
                return false;
            if (reference.Instance is null)
                return IsInContainingTypes(field.ContainingType) && !InInitializer(isStatic: true);
            return reference.Instance
                    is IInstanceReferenceOperation
                    {
                        ReferenceKind: InstanceReferenceKind.ContainingTypeInstance,
                    } instance
                && IsPure(instance)
                && !InInitializer(isStatic: false);
        }

        private bool IsInContainingTypes(INamedTypeSymbol type)
        {
            for (var candidate = _containingType.Value; candidate is not null; )
            {
                if (
                    SymbolEqualityComparer.Default.Equals(
                        candidate.OriginalDefinition,
                        type.OriginalDefinition
                    )
                )
                    return true;
                candidate = candidate.ContainingType;
            }
            return false;
        }

        private bool InInitializer(bool isStatic) =>
            declaration
                .Ancestors()
                .Any(node =>
                    node is ConstructorDeclarationSyntax constructor
                        && constructor.Modifiers.Any(SyntaxKind.StaticKeyword) == isStatic
                    || !isStatic
                        && node
                            is AccessorDeclarationSyntax
                            {
                                RawKind: (int)SyntaxKind.InitAccessorDeclaration
                            }
                );

        // Neither written by the initializer nor captured by a nested function that it could run.
        private bool IsStable(ISymbol variable) =>
            _initializerFlow.Value is { } flow
            && !flow.WrittenInside.Contains(variable, SymbolEqualityComparer.Default)
            && !IsCaptured(variable);

        private bool IsCaptured(ISymbol variable)
        {
            var declared = variable.DeclaringSyntaxReferences.Select(reference => reference.Span);
            var scope =
                declaration
                    .Ancestors()
                    .FirstOrDefault(node =>
                        node
                            is MemberDeclarationSyntax
                                and not BaseTypeDeclarationSyntax
                                and not BaseNamespaceDeclarationSyntax
                                and not GlobalStatementSyntax
                    )
                ?? source.Tree.GetRoot(source.Project.CancellationToken);
            return scope
                .DescendantNodes()
                .Where(node =>
                    node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax
                    && !declared.Any(span => node.Span.Contains(span))
                )
                .Any(function =>
                    function
                        .DescendantNodes()
                        .OfType<IdentifierNameSyntax>()
                        .Any(name =>
                            name.Identifier.ValueText == variable.Name
                            && SymbolEqualityComparer.Default.Equals(
                                source
                                    .Model.GetSymbolInfo(name, source.Project.CancellationToken)
                                    .Symbol,
                                variable
                            )
                        )
                );
        }
    }
}
