using DrillPress.Operations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress.Flow;

internal static class NullableProbe
{
    internal static NullableFlowState BeforeCheck(
        CodeExpression expression,
        CodeExpression condition
    )
    {
        var statement = condition.Syntax.Ancestors().OfType<StatementSyntax>().FirstOrDefault();
        if (!AtEntry(statement, condition.Syntax))
            return NullableFlowState.None;
        var name = "__drillPressNullableProbe";
        while (expression.Source.Model.LookupSymbols(statement!.SpanStart, name: name).Length != 0)
            name += "_";
        var value = expression.Syntax.WithoutTrivia();
        var declaration = SyntaxFactory.LocalDeclarationStatement(
            SyntaxFactory
                .VariableDeclaration(SyntaxFactory.IdentifierName("var"))
                .WithVariables(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory
                            .VariableDeclarator(name)
                            .WithInitializer(SyntaxFactory.EqualsValueClause(value))
                    )
                )
        );
        if (
            !expression.Source.Model.TryGetSpeculativeSemanticModel(
                statement!.SpanStart,
                declaration,
                out var model
            )
        )
            return NullableFlowState.None;
        var initializer = declaration.Declaration.Variables[0].Initializer!.Value;
        return model
            .GetTypeInfo(initializer, expression.Source.Project.CancellationToken)
            .Nullability.FlowState;
    }

    private static bool AtEntry(StatementSyntax? statement, ExpressionSyntax condition) =>
        statement switch
        {
            IfStatementSyntax branch => branch.Condition == condition,
            ReturnStatementSyntax returned => returned.Expression == condition,
            LocalDeclarationStatementSyntax local => local.Declaration.Variables.Count == 1
                && local.Declaration.Variables[0].Initializer?.Value == condition,
            ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax
                {
                    Left: IdentifierNameSyntax,
                    Right: var right
                }
            } => right == condition,
            _ => false,
        };
}
