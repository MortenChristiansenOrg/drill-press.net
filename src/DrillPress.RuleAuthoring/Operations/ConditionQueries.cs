using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress.Operations;

/// <summary>Composable condition selection without framework-specific validation policy.</summary>
public static class ConditionQueries
{
    /// <summary>Selects complete if and conditional-expression tests within the executable scope boundary.</summary>
    public static CodeQuery<CodeCondition> Conditions(this CodeQuery<CodeBody> bodies) =>
        bodies
            .Nodes<SyntaxNode>()
            .SelectMany(node =>
                node.Syntax switch
                {
                    IfStatementSyntax statement => new[]
                    {
                        new CodeCondition(
                            new(node.Source, statement.Condition),
                            statement.Statement,
                            statement.Else?.Statement
                        ),
                    },
                    ConditionalExpressionSyntax expression =>
                    [
                        new CodeCondition(
                            new(node.Source, expression.Condition),
                            expression.WhenTrue,
                            expression.WhenFalse
                        ),
                    ],
                    _ => [],
                }
            );

    /// <summary>Matches complete checks; unsupported compound or overloaded Boolean shapes produce no evidence.</summary>
    public static CodeQuery<ConditionMatch> Checks(
        this CodeQuery<CodeCondition> conditions,
        params ConditionPattern[] patterns
    )
    {
        var configured = patterns.ToArray();
        return conditions.SelectMany(condition =>
            configured.Select(pattern => pattern.Match(condition)).OfType<ConditionMatch>()
        );
    }
}
