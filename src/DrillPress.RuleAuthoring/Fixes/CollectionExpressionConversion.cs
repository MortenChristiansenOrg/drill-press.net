using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>Converts standard collection initialization to a collection expression while preserving concrete type and element evaluation.</summary>
public sealed class CollectionExpressionConversion
{
    private readonly CodeExpression _expression;

    internal CollectionExpressionConversion(CodeExpression expression) => _expression = expression;

    /// <summary>Proposes a collection expression only when the concrete type stays the same and every retained element is evaluated once in order.</summary>
    public FixProposal? Propose() => Convert(_expression);

    private static FixProposal? Convert(CodeExpression expression)
    {
        if (
            !IsCollectionInitialisation(expression.Syntax)
            || !IsTargetTyped(expression.Syntax)
            || !CreatesCollection(expression)
            || !AcceptsCollectionExpression(expression)
        )
            return FixProposal.Withheld(
                "the expression is not a supported target-typed collection initializer"
            );
        var elements = expression.Syntax switch
        {
            BaseObjectCreationExpressionSyntax creation => creation.Initializer?.Expressions,
            ArrayCreationExpressionSyntax array => array.Initializer?.Expressions,
            ImplicitArrayCreationExpressionSyntax array => array.Initializer.Expressions,
            _ => null,
        };
        // Keep the concrete collection. An interface target can choose a different implementation, so that finding needs
        // a deliberate edit. CreatesCollection excludes nonempty dictionary initializers, whose elements need a builder.
        if (
            expression.TypeInfo is not { Type: { } created, ConvertedType: { } target }
            || !SymbolEqualityComparer.Default.Equals(created, target)
        )
        {
            return FixProposal.Withheld(
                "the collection expression would change the concrete collection"
            );
        }
        var kept =
            elements?.Select(element => new CodeExpression(expression.Source, element)).ToArray()
            ?? [];
        var replacement =
            "[" + string.Join(", ", kept.Select((_, index) => "{" + index + "}")) + "]";
        var fix = Fix.For(expression).ReplaceWith(replacement, kept);
        fix = fix.MustPreserve(
            ExpressionBehavior.EvaluationCounts | ExpressionBehavior.EvaluationOrder
        );
        return fix.SafeWhen(change =>
            CreatesCollection(change.Before)
            && change.After.Syntax is CollectionExpressionSyntax
            && change.Same(
                change.Before.TypeInfo.ConvertedType,
                change.After.TypeInfo.ConvertedType
            )
        );
    }

    internal static bool IsCollectionInitialisation(ExpressionSyntax syntax) =>
        syntax
            is BaseObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0 }
                or BaseObjectCreationExpressionSyntax { ArgumentList: null }
                or ArrayCreationExpressionSyntax
                or ImplicitArrayCreationExpressionSyntax
                or InvocationExpressionSyntax { ArgumentList.Arguments.Count: 0 };

    // Only where the declared type is the target: a variable or property with a written type, an assignment, or a return
    // from a member. Elsewhere, such as in arguments or lambdas, [] can change the overload or has no type.
    internal static bool IsTargetTyped(ExpressionSyntax syntax) =>
        syntax.Parent switch
        {
            EqualsValueClauseSyntax
            {
                Parent: VariableDeclaratorSyntax
                    {
                        Parent: VariableDeclarationSyntax { Type.IsVar: false }
                    }
                    or PropertyDeclarationSyntax
            } => true,
            AssignmentExpressionSyntax assignment => assignment.IsKind(
                SyntaxKind.SimpleAssignmentExpression
            )
                && assignment.Right == syntax,
            ArrowExpressionClauseSyntax { Parent: not LocalFunctionStatementSyntax } => true,
            ReturnStatementSyntax => !syntax
                .Ancestors()
                .Any(ancestor =>
                    ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax
                ),
            _ => false,
        };

    private static readonly HashSet<string> _listLikeTypes =
    [
        "System.Collections.Generic.List<T>",
        "System.Collections.Generic.HashSet<T>",
        "System.Collections.Generic.Dictionary<TKey, TValue>",
    ];

    private static readonly HashSet<string> _collectionInterfaces =
    [
        "System.Collections.Generic.IEnumerable<T>",
        "System.Collections.Generic.IReadOnlyCollection<T>",
        "System.Collections.Generic.IReadOnlyList<T>",
        "System.Collections.Generic.ICollection<T>",
        "System.Collections.Generic.IList<T>",
    ];

    // The target is the created type itself or a collection interface that [] can build; a target such as object, as in a
    // dictionary of JSON values, gives [] no type.
    internal static bool AcceptsCollectionExpression(CodeExpression expression) =>
        expression.TypeInfo is { Type: { } created, ConvertedType: { } target }
        && (
            SymbolEqualityComparer.Default.Equals(created, target)
            || target is INamedTypeSymbol { TypeKind: TypeKind.Interface } collection
                && _collectionInterfaces.Contains(collection.OriginalDefinition.ToDisplayString())
        );

    // The collections that a collection expression creates in the same way: a List, HashSet or Dictionary without
    // constructor arguments, with no initialiser or only plain elements, an empty array, and array initialisers.
    internal static bool CreatesCollection(CodeExpression expression)
    {
        var model = expression.Source.Model;
        return expression.Syntax switch
        {
            BaseObjectCreationExpressionSyntax creation => model.GetTypeInfo(creation).Type
                is INamedTypeSymbol type
                && _listLikeTypes.Contains(type.OriginalDefinition.ToDisplayString())
                && (
                    creation.Initializer is null
                    || creation.Initializer.IsKind(SyntaxKind.CollectionInitializerExpression)
                        && type.OriginalDefinition.Arity == 1
                        && creation.Initializer.Expressions.All(element =>
                            element is not InitializerExpressionSyntax
                        )
                ),
            ArrayCreationExpressionSyntax array => model.GetTypeInfo(array).Type
                is IArrayTypeSymbol { Rank: 1 }
                && (
                    array.Initializer is not null
                    || array.Type.RankSpecifiers
                        is [{ Sizes: [LiteralExpressionSyntax { Token.ValueText: "0" }] }]
                ),
            ImplicitArrayCreationExpressionSyntax array => model.GetTypeInfo(array).Type
                is IArrayTypeSymbol { Rank: 1 },
            InvocationExpressionSyntax invocation => model.GetSymbolInfo(invocation).Symbol
                is IMethodSymbol
                {
                    Name: "Empty",
                    ContainingType.SpecialType: SpecialType.System_Array
                },
            _ => false,
        };
    }
}
