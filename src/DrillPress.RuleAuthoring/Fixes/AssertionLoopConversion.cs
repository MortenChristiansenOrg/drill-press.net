using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>Converts a synchronous foreach containing one xUnit assertion to Assert.All. Passing tests keep the same assertions; failing tests can aggregate failures instead of stopping at the first item.</summary>
public sealed class AssertionLoopConversion
{
    private static readonly CodeType _assert = CodeType.Named("Xunit.Assert");
    private readonly CodeNode<StatementSyntax> _loop;

    internal AssertionLoopConversion(AnalysisSource source, StatementSyntax statement) =>
        _loop = new(source, statement);

    /// <summary>Proposes Assert.All for a var foreach over IEnumerable of its exact element type, retaining assertion binding. Awaits, ref expressions and pattern-only enumerators are withheld.</summary>
    public FixProposal? Propose() => Convert(_loop);

    // `foreach (var item in items) Assert.X(...);` becomes `Assert.All(items, item => Assert.X(...));`. Assert.All checks
    // every item and reports all failures; a passing test behaves the same.
    private static FixProposal? Convert(CodeNode<StatementSyntax> loop)
    {
        if (
            loop.Syntax
                is not ForEachStatementSyntax
                {
                    AwaitKeyword.RawKind: 0,
                    Type.IsVar: true
                } statement
            || SingleStatement(statement.Statement)
                is not ExpressionStatementSyntax
                {
                    Expression: InvocationExpressionSyntax assertion
                }
            || loop.Source.Model.GetForEachStatementInfo(statement)
                is not { ElementType: { } element }
            || loop.Source.Model.GetTypeInfo(statement.Expression).Type is not { } collection
            || !ImplementsEnumerableOf(collection, element)
            || loop.Source.Model.GetSymbolInfo(assertion).Symbol is not IMethodSymbol asserted
            || !_assert.Matches(asserted.ContainingType)
            || SyntaxScopes
                .OwnNodes(assertion, includeSelf: true)
                .Any(node => node is AwaitExpressionSyntax or RefExpressionSyntax)
        )
        {
            return null;
        }
        var replacement =
            $"Assert.All({statement.Expression.ToFullString().Trim()}, {statement.Identifier.ValueText} => {assertion.ToFullString().Trim()});";
        var source = loop.Source;
        return SourceChanges.Propose(
            [SourceChanges.Replace(source, statement.Span, replacement)],
            context =>
                context.Map(source, statement)
                    is {
                        After: ExpressionStatementSyntax
                        {
                            Expression: InvocationExpressionSyntax all
                        }
                    } mapped
                && mapped.Model.GetSymbolInfo(all).Symbol
                    is IMethodSymbol { Name: "All", TypeArguments: [var item] } method
                && _assert.Matches(method.ContainingType)
                && method.Parameters
                    is [
                        { Type: INamedTypeSymbol { Name: "IEnumerable" } },
                        { Type: INamedTypeSymbol { Name: "Action" } },
                    ]
                && context.Same(element, item)
                && all.ArgumentList.Arguments[1].Expression
                    is SimpleLambdaExpressionSyntax { Body: InvocationExpressionSyntax inner }
                && mapped.Model.GetSymbolInfo(inner).Symbol is IMethodSymbol after
                && context.Same(asserted, after)
        );
    }

    private static StatementSyntax SingleStatement(StatementSyntax statement) =>
        statement is BlockSyntax { Statements: [var only] } ? only : statement;

    private static bool ImplementsEnumerableOf(ITypeSymbol collection, ITypeSymbol element) =>
        collection is IArrayTypeSymbol array
            && SymbolEqualityComparer.Default.Equals(array.ElementType, element)
        || collection
            .AllInterfaces.Concat(collection is INamedTypeSymbol named ? [named] : [])
            .Any(type =>
                type is { Name: "IEnumerable", TypeArguments: [var argument] }
                && type.ContainingNamespace.ToDisplayString() == "System.Collections.Generic"
                && SymbolEqualityComparer.Default.Equals(argument, element)
            );
}
