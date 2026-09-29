using DrillPress;
using Microsoft.CodeAnalysis;

namespace DrillPress.Collections;

/// <summary>Bounded grouping over explicitly selected expressions, cached once per analysis/query.</summary>
public static class ExpressionGroups
{
    /// <summary>Associates each expression with all selected groups; an absent or ambiguous group never removes the finding.</summary>
    public static CodeQuery<ExpressionMembership> WithGroupsFrom(
        this CodeQuery<CodeExpression> expressions,
        CodeQuery<ExpressionGroup> groups
    ) =>
        CodeQuery<ExpressionMembership>.Create(solution =>
        {
            var memberships = groups
                .In(solution)
                .SelectMany(group =>
                    group.Occurrences.Select(occurrence =>
                        (
                            Key: (occurrence.Expression.Source, occurrence.Expression.Syntax.Span),
                            Group: group
                        )
                    )
                )
                .ToLookup(item => item.Key, item => item.Group);
            return expressions
                .In(solution)
                .Select(expression => new ExpressionMembership(
                    expression,
                    Array.AsReadOnly(
                        memberships[(expression.Source, expression.Syntax.Span)]
                            .Distinct()
                            .ToArray()
                    )
                ));
        });

    /// <summary>Groups equal typed compiler constants within each containing type/context, including partial declarations and present null values.</summary>
    /// <param name="expressions">Only these selected occurrences participate; duplicate inputs are removed.</param>
    /// <param name="minimumOccurrences">The minimum number of distinct selected occurrences per group.</param>
    /// <param name="additionalEquivalence">An optional stricter equivalence relation, compared against each partition's representative.</param>
    public static CodeQuery<ExpressionGroup> Constants(
        CodeQuery<CodeExpression> expressions,
        int minimumOccurrences = 2,
        Func<CodeExpression, CodeExpression, bool>? additionalEquivalence = null
    ) => Group(expressions, null, minimumOccurrences, additionalEquivalence);

    /// <summary>Groups supported one-hole string expressions while retaining a typed capture per occurrence. Two reads of the same variable are two holes and are rejected.</summary>
    /// <param name="expressions">Only these selected occurrences participate; duplicate inputs are removed.</param>
    /// <param name="options">Explicit syntax, capture, API and complexity boundaries.</param>
    /// <param name="minimumOccurrences">The minimum number of distinct selected occurrences per group.</param>
    /// <param name="additionalEquivalence">An optional stricter equivalence relation, compared against each partition's representative.</param>
    public static CodeQuery<ExpressionGroup> OneHoleTemplates(
        CodeQuery<CodeExpression> expressions,
        OneHoleTemplateOptions options,
        int minimumOccurrences = 2,
        Func<CodeExpression, CodeExpression, bool>? additionalEquivalence = null
    ) => Group(expressions, options, minimumOccurrences, additionalEquivalence);

    private static CodeQuery<ExpressionGroup> Group(
        CodeQuery<CodeExpression> expressions,
        OneHoleTemplateOptions? options,
        int minimum,
        Func<CodeExpression, CodeExpression, bool>? equivalent
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minimum, 1);
        return CodeQuery<ExpressionGroup>.Create(solution =>
        {
            var groups =
                new Dictionary<
                    (CodeDeclaration Owner, string Shape),
                    List<List<ExpressionOccurrence>>
                >();
            foreach (var (owner, expression, shape) in Candidates(expressions, options, solution))
            {
                var key = (owner, shape.Key);
                if (!groups.TryGetValue(key, out var partitions))
                    groups.Add(key, partitions = []);
                var partition = partitions.FirstOrDefault(group =>
                    equivalent?.Invoke(group[0].Expression, expression) ?? true
                );
                if (partition is null)
                    partitions.Add(partition = []);
                partition.Add(new(expression, shape.Capture));
            }
            return groups.SelectMany(pair =>
                pair.Value.Where(group => group.Count >= minimum)
                    .Select(group => new ExpressionGroup(
                        pair.Key.Owner,
                        options is null
                            ? ExpressionGroupKind.Constant
                            : ExpressionGroupKind.OneHoleTemplate,
                        group.AsReadOnly(),
                        options
                    ))
            );
        });
    }

    private static IEnumerable<(
        CodeDeclaration Owner,
        CodeExpression Expression,
        ExpressionShape Shape
    )> Candidates(
        CodeQuery<CodeExpression> expressions,
        OneHoleTemplateOptions? options,
        AnalysisSolution solution
    )
    {
        var owners = solution
            .Types.GroupBy(declaration => declaration.Source.Project)
            .ToDictionary(
                group => group.Key,
                group =>
                    group.ToDictionary(
                        declaration => (ISymbol)declaration.Symbol,
                        declaration => declaration,
                        SymbolEqualityComparer.Default
                    )
            );
        foreach (
            var expression in expressions
                .In(solution)
                .DistinctBy(expression => (expression.Source, expression.Syntax.Span))
                .OrderBy(expression => expression.Source.Document.Path, StringComparer.Ordinal)
                .ThenBy(expression => expression.Syntax.SpanStart)
        )
        {
            solution.CancellationToken.ThrowIfCancellationRequested();
            var enclosing = expression.Source.Model.GetEnclosingSymbol(
                expression.Syntax.SpanStart,
                solution.CancellationToken
            );
            var owner = enclosing as INamedTypeSymbol ?? enclosing?.ContainingType;
            if (
                owner is not null
                && owners.TryGetValue(expression.Source.Project, out var contextualOwners)
                && contextualOwners.TryGetValue(owner.OriginalDefinition, out var declaration)
                && ExpressionShape.Read(expression, options) is { } shape
            )
                yield return (declaration, expression, shape);
        }
    }
}
