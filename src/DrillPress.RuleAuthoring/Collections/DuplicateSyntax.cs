using DrillPress.Queries;
using Microsoft.CodeAnalysis;

namespace DrillPress.Collections;

/// <summary>Exact token-shape duplication, ignoring trivia but preserving identifier spelling and literals. This is evidence of repetition, not semantic equivalence.</summary>
public static class DuplicateSyntax
{
    /// <summary>Selects every occurrence of a repeated syntax shape with at least the requested token count. Groups stay within each compilation context.</summary>
    public static CodeQuery<CodeNode<TSyntax>> In<TSyntax>(
        CodeQuery<CodeNode<TSyntax>> query,
        int minimumTokens = 12
    )
        where TSyntax : SyntaxNode
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumTokens, 1);
        return CodeQuery<CodeNode<TSyntax>>.Create(solution =>
            query
                .In(solution)
                .Select(node => (Node: node, Tokens: node.Syntax.DescendantTokens().ToArray()))
                .Where(item => item.Tokens.Length >= minimumTokens)
                .GroupBy(item =>
                    (
                        item.Node.Source.Project,
                        Shape: string.Join(
                            "|",
                            item.Tokens.Select(token =>
                                $"{token.RawKind}:{token.Text.Length}:{token.Text}"
                            )
                        )
                    )
                )
                .Where(group => group.Skip(1).Any())
                .SelectMany(group => group.Select(item => item.Node))
        );
    }
}
