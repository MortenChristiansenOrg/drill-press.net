using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

internal sealed class ProjectCoverage
{
    private readonly Dictionary<string, CoverageRange[]> _documents = [];

    internal void Add(string documentId, IEnumerable<CoverageRange> ranges) =>
        _documents[documentId] = ranges
            .GroupBy(range => (range.Span, range.FunctionIdentity))
            .Select(group => new CoverageRange(
                group.Key.Span,
                MergePoints(group),
                group.Key.FunctionIdentity
            ))
            .GroupBy(range => range.Span)
            .Select(group => new CoverageRange(group.Key, MergePoints(group), ""))
            .ToArray();

    private static ExecutionCoverage MergePoints(IEnumerable<CoverageRange> ranges) =>
        ranges.All(range => range.State == ExecutionCoverage.Covered) ? ExecutionCoverage.Covered
        : ranges.All(range => range.State == ExecutionCoverage.Uncovered)
            ? ExecutionCoverage.Uncovered
        : ExecutionCoverage.Unknown;

    internal ExecutionCoverage ExecutionOf(ICodeElement element)
    {
        if (
            element.Source is not { } source
            || !_documents.TryGetValue(source.Document.DocumentId, out var ranges)
        )
            return ExecutionCoverage.Unknown;
        var span = new TextSpan(element.Location.Start, element.Location.Length);
        var matching = ranges.Where(range => range.Span.Contains(span)).ToArray();
        if (matching.Length == 0)
            return ExecutionCoverage.Unknown;
        if (matching.All(range => range.State == ExecutionCoverage.Uncovered))
            return ExecutionCoverage.Uncovered;
        if (matching.Any(range => range.State == ExecutionCoverage.Unknown))
            return ExecutionCoverage.Unknown;
        // Sequence points can cover both sides of a branch. A hit cannot identify its conditional arm.
        var node = source
            .Tree.GetRoot(source.Project.CancellationToken)
            .FindNode(span, getInnermostNodeForTie: true);
        foreach (var range in matching.Where(range => range.State == ExecutionCoverage.Covered))
        {
            if (range.Span == span)
                return ExecutionCoverage.Covered;
            var anchor = source.Tree.GetRoot().FindNode(range.Span, getInnermostNodeForTie: true);
            foreach (var expression in DirectExpressions(anchor))
            {
                if (expression.Span == span)
                    return ExecutionCoverage.Covered;
                // Member-reference selectors omit the invocation arguments; accept only an argument-free direct call.
                if (
                    expression is InvocationExpressionSyntax invocation
                    && invocation.Expression.Span == node.Span
                    && invocation.ArgumentList.Arguments.Count == 0
                )
                    return ExecutionCoverage.Covered;
            }
        }
        return ExecutionCoverage.Unknown;
    }

    private static IEnumerable<ExpressionSyntax> DirectExpressions(
        Microsoft.CodeAnalysis.SyntaxNode anchor
    )
    {
        ExpressionSyntax? expression = anchor switch
        {
            ExpressionSyntax value => value,
            ExpressionStatementSyntax statement => statement.Expression,
            ReturnStatementSyntax statement => statement.Expression,
            ThrowStatementSyntax statement => statement.Expression,
            LocalDeclarationStatementSyntax { Declaration.Variables.Count: 1 } statement =>
                statement.Declaration.Variables[0].Initializer?.Value,
            _ => null,
        };
        while (expression is not null)
        {
            yield return expression;
            expression = expression switch
            {
                AwaitExpressionSyntax awaited => awaited.Expression,
                ParenthesizedExpressionSyntax parentheses => parentheses.Expression,
                CheckedExpressionSyntax checkedExpression => checkedExpression.Expression,
                _ => null,
            };
        }
    }

    internal CoverageMeasurement Measure(
        IEnumerable<AnalysisSource> sources,
        SourceLocation? location = null
    )
    {
        var covered = 0;
        var total = 0;
        var complete = true;
        foreach (var source in sources)
        {
            if (!_documents.TryGetValue(source.Document.DocumentId, out var ranges))
            {
                complete = false;
                continue;
            }
            var text = source.Tree.GetText();
            var lines = new Dictionary<int, bool>();
            foreach (
                var range in ranges.Where(range =>
                    location is null
                    || range.Span.OverlapsWith(new(location.Start, location.Length))
                )
            )
            {
                var start = text.Lines.GetLineFromPosition(range.Span.Start).LineNumber;
                var end = text
                    .Lines.GetLineFromPosition(Math.Max(range.Span.Start, range.Span.End - 1))
                    .LineNumber;
                for (var line = start; line <= end; line++)
                    lines[line] =
                        lines.GetValueOrDefault(line, true)
                        && range.State == ExecutionCoverage.Covered;
            }
            covered += lines.Count(line => line.Value);
            total += lines.Count;
        }
        return new(covered, total, complete);
    }
}
