using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

// The evaluation an inlined initializer moves past within the read's statement, and whether that evaluation is
// pure enough for the move to be unobservable: it neither throws nor observes the initializer's effects.
internal static class InlineEvaluationOrder
{
    // The outermost operations of the header that run before the read: earlier operands, implicit receivers
    // and the creation or copy of an object whose initializer contains the read.
    internal static IReadOnlyList<IOperation> Before(InlineReadPosition position)
    {
        var ancestors = new HashSet<IOperation>();
        var before = new List<IOperation>();
        for (var operation = position.Read; operation is not null; operation = operation.Parent)
        {
            ancestors.Add(operation);
            if (ConstructedFirst(operation) is { } constructed)
                before.Add(constructed);
            if (operation == position.Header)
                break;
        }
        Collect(position.Header, position.Read.Syntax.SpanStart, ancestors, before);
        return before.AsReadOnly();
    }

    // A constructor or clone runs before the initializer it is written with, and a collection expression
    // with a constructor creates its collection before adding the elements.
    private static IOperation? ConstructedFirst(IOperation operation) =>
        operation switch
        {
            IObjectOrCollectionInitializerOperation
            {
                Parent: IObjectCreationOperation
                    or ITypeParameterObjectCreationOperation
                    or IDynamicObjectCreationOperation
                    or IWithOperation
            } initializer => initializer.Parent,
            ICollectionExpressionOperation
            {
                ConstructMethod: IMethodSymbol { MethodKind: MethodKind.Constructor },
            } collection => collection,
            _ => null,
        };

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

        // Instance readonly fields change only in constructors and init accessors. Reading a static field may
        // run its type's initializer, even for the containing type, so it is not pure.
        private bool IsReadOnlyField(IFieldReferenceOperation reference)
        {
            var field = reference.Field;
            if (field.IsConst)
                return true;
            if (!field.IsReadOnly || reference.Instance is null)
                return false;
            return reference.Instance
                    is IInstanceReferenceOperation
                    {
                        ReferenceKind: InstanceReferenceKind.ContainingTypeInstance,
                    } instance
                && IsPure(instance)
                && !InInstanceInitializer();
        }

        private bool InInstanceInitializer() =>
            declaration
                .Ancestors()
                .Any(node =>
                    node is ConstructorDeclarationSyntax constructor
                        && !constructor.Modifiers.Any(SyntaxKind.StaticKeyword)
                    || node
                        is AccessorDeclarationSyntax
                        {
                            RawKind: (int)SyntaxKind.InitAccessorDeclaration
                        }
                );

        // Neither written by the initializer nor reachable through a nested function or a ref alias that it
        // could use to write the variable.
        private bool IsStable(ISymbol variable) =>
            _initializerFlow.Value is { } flow
            && !flow.WrittenInside.Contains(variable, SymbolEqualityComparer.Default)
            && !IsShared(variable);

        private bool IsShared(ISymbol variable)
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
                .OfType<IdentifierNameSyntax>()
                .Where(name =>
                    name.Identifier.ValueText == variable.Name
                    && (IsAliased(name) || IsInNestedFunction(name, declared))
                )
                .Any(name =>
                    SymbolEqualityComparer.Default.Equals(
                        source.Model.GetSymbolInfo(name, source.Project.CancellationToken).Symbol,
                        variable
                    )
                );
        }

        private static bool IsInNestedFunction(SyntaxNode name, IEnumerable<TextSpan> declared) =>
            name.Ancestors()
                .Any(node =>
                    node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax
                    && !declared.Any(span => node.Span.Contains(span))
                );

        // A reference passed by ref or out, taken by ref or by address, whose alias may later write it.
        private static bool IsAliased(ExpressionSyntax name)
        {
            SyntaxNode node = name;
            while (node.Parent is ParenthesizedExpressionSyntax parent)
                node = parent;
            return node.Parent switch
            {
                RefExpressionSyntax => true,
                ArgumentSyntax argument => argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword)
                    || argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword),
                PrefixUnaryExpressionSyntax prefix => prefix.IsKind(SyntaxKind.AddressOfExpression),
                _ => false,
            };
        }
    }
}
