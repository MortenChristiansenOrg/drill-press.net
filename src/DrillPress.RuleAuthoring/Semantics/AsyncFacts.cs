using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

/// <summary>Facts about framework task types, method contracts and exception scopes. These are syntax and compiler facts, not control-flow guarantees.</summary>
public static class AsyncFacts
{
    /// <summary>Tests the exact containing namespace; does not include child namespaces.</summary>
    public static bool IsInNamespace(ISymbol? symbol, string name) =>
        symbol?.ContainingNamespace?.ToDisplayString() == name;

    /// <summary>Tests the original declaration name and namespace, ignoring constructed arguments.</summary>
    public static bool IsType(ITypeSymbol? type, string containingNamespace, string name) =>
        type is not null
        && type.OriginalDefinition.Name == name
        && IsInNamespace(type.OriginalDefinition, containingNamespace);

    /// <summary>Task, Task&lt;T&gt;, ValueTask and ValueTask&lt;T&gt;.</summary>
    public static bool IsTask(ITypeSymbol? type) =>
        IsType(type, "System.Threading.Tasks", "Task")
        || IsType(type, "System.Threading.Tasks", "ValueTask");

    /// <summary>The result type of Task&lt;T&gt; or ValueTask&lt;T&gt;, otherwise the original type. Does not infer custom awaiter patterns.</summary>
    public static ITypeSymbol UnwrapTask(ITypeSymbol type) =>
        type is INamedTypeSymbol { TypeArguments: [var result] } && IsTask(type) ? result : type;

    /// <summary>Recognizes Task, ValueTask and IAsyncEnumerable return types, including generic task forms.</summary>
    public static bool IsAsyncReturnType(ITypeSymbol? type) =>
        IsTask(type) || IsType(type, "System.Collections.Generic", "IAsyncEnumerable");

    /// <summary>Overrides and interface implementations, whose signature and names another declaration decides.</summary>
    public static bool IsDictatedBySignatureElsewhere(IMethodSymbol method)
    {
        if (method.IsOverride || method.ExplicitInterfaceImplementations.Length > 0)
        {
            return true;
        }
        var type = method.ContainingType;
        if (type is null)
        {
            return false;
        }
        foreach (var contract in type.AllInterfaces)
        {
            foreach (var member in contract.GetMembers(method.Name).OfType<IMethodSymbol>())
            {
                if (
                    SymbolEqualityComparer.Default.Equals(
                        type.FindImplementationForInterfaceMember(member),
                        method
                    )
                )
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Whether a compilation unit contains a global statement.</summary>
    public static bool HasTopLevelStatements(SyntaxTree tree) =>
        tree.GetRoot() is CompilationUnitSyntax unit
        && unit.Members.OfType<GlobalStatementSyntax>().Any();

    /// <summary>Whether syntax declares a method, accessor, local function or anonymous function.</summary>
    public static bool IsFunctionBoundary(SyntaxNode node) =>
        node
            is AnonymousFunctionExpressionSyntax
                or LocalFunctionStatementSyntax
                or BaseMethodDeclarationSyntax
                or AccessorDeclarationSyntax;

    /// <summary>A catch that also catches unexpected exceptions: untyped or <see cref="Exception"/>, with or without a filter.</summary>
    public static bool CatchesAll(CatchClauseSyntax clause, SemanticModel model) =>
        clause.Declaration is null
        || IsType(model.GetTypeInfo(clause.Declaration.Type).Type, "System", "Exception");

    /// <summary>Whether a containing try block has a broad catch (untyped or System.Exception). Filters are deliberately not evaluated.</summary>
    public static bool IsGuardedBy(
        TryStatementSyntax tryStatement,
        SyntaxNode node,
        SemanticModel model
    ) =>
        tryStatement.Block.Span.Contains(node.Span)
        && tryStatement.Catches.Any(clause => CatchesAll(clause, model));

    /// <summary>
    /// The finally or catch clause the awaited work runs in, unless a try/catch inside that clause handles its failure. Null
    /// when the await isn't in one, or is in a nested function.
    /// </summary>
    public static SyntaxNode? UnguardedHandler(AwaitExpressionSyntax await, SemanticModel model)
    {
        foreach (var ancestor in await.Ancestors())
        {
            if (IsFunctionBoundary(ancestor))
            {
                return null;
            }
            if (
                ancestor is TryStatementSyntax tryStatement
                && IsGuardedBy(tryStatement, await, model)
            )
            {
                return null;
            }
            if (ancestor is FinallyClauseSyntax or CatchClauseSyntax)
            {
                return ancestor;
            }
        }
        return null;
    }

    /// <summary>Whether a surrounding try block before the function boundary has a broad catch. Filters are deliberately not evaluated.</summary>
    public static bool IsGuardedInFunction(SyntaxNode node, SemanticModel model)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (IsFunctionBoundary(ancestor))
            {
                return false;
            }
            if (
                ancestor is TryStatementSyntax tryStatement
                && IsGuardedBy(tryStatement, node, model)
            )
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Whether an enclosing loop appears before the nearest function boundary.</summary>
    public static bool IsInLoop(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (IsFunctionBoundary(ancestor))
            {
                return false;
            }
            if (
                ancestor
                is WhileStatementSyntax
                    or DoStatementSyntax
                    or ForStatementSyntax
                    or CommonForEachStatementSyntax
            )
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The call an await expression awaits, looking through ConfigureAwait.</summary>
    public static IInvocationOperation? AwaitedCall(IOperation? awaited)
    {
        var operation = awaited is IAwaitOperation await ? await.Operation : awaited;
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }
        if (
            operation is IInvocationOperation
            {
                TargetMethod.Name: "ConfigureAwait",
                Instance: { } configured
            }
        )
        {
            operation = configured;
        }
        return operation as IInvocationOperation;
    }

    /// <summary>Recognizes the object sender/EventArgs-derived two-parameter event-handler signature.</summary>
    public static bool IsEventHandler(IMethodSymbol method) =>
        method.Parameters
            is [{ Type.SpecialType: SpecialType.System_Object }, { Type: var arguments }]
        && DerivesFromEventArgs(arguments);

    private static bool DerivesFromEventArgs(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (IsType(current, "System", "EventArgs"))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Whether an anonymous function is directly assigned with +=; does not infer whether the target is an event.</summary>
    public static bool IsEventSubscription(SyntaxNode function) =>
        function.Parent is AssignmentExpressionSyntax assignment
        && assignment.IsKind(SyntaxKind.AddAssignmentExpression);

    /// <summary>The task-typed expression an expression statement drops: an unawaited call or a discard assignment.</summary>
    public static ExpressionSyntax? ForgottenTask(
        ExpressionStatementSyntax statement,
        SemanticModel model
    )
    {
        var expression = statement.Expression;
        if (expression is AssignmentExpressionSyntax assignment)
        {
            // A task kept in a variable or field can be awaited later; only a discard drops it.
            if (
                assignment.Left is not IdentifierNameSyntax { Identifier.ValueText: "_" } discard
                || model.GetSymbolInfo(discard).Symbol is not (null or IDiscardSymbol)
            )
            {
                return null;
            }
            expression = assignment.Right;
        }
        if (expression is AwaitExpressionSyntax)
        {
            return null;
        }
        return IsTask(model.GetTypeInfo(expression).Type) ? expression : null;
    }
}
