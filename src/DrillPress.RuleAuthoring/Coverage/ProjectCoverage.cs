using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

internal sealed class ProjectCoverage
{
    private readonly Dictionary<string, CoverageRange[]> _documents = [];
    private readonly Dictionary<(string Document, TextSpan Span), CoverageEvidence> _precise = [];

    internal void PreciseExecution(
        AnalysisSource source,
        TextSpan occurrence,
        ExecutionCoverage state,
        CoverageReason[] reasons,
        CoverageRange[] ranges,
        CoverageCallEvidence[] calls
    ) =>
        _precise[(source.Document.DocumentId, occurrence)] = Evidence(
            source,
            state,
            reasons,
            ranges
        ) with
        {
            Calls = calls.Length == 0 ? null : Array.AsReadOnly(calls),
        };

    private readonly Dictionary<string, CoverageRange[]> _functionRanges = [];
    private readonly HashSet<(string Document, TextSpan Span, int Token)> _advancements = [];
    private readonly Dictionary<string, CoverageReason> _documentReasons = [];
    private CoverageReason _unavailableReason = CoverageReason.EvidenceNotPrepared;
    private readonly Dictionary<
        (string Document, TextSpan Span, CoverageMetric Metric),
        (ExecutionCoverage State, CoverageReason[] Reasons)
    > _fixtureExecutions = [];
    private readonly Dictionary<string, LineCoverageMeasurement> _fixtureLines = [];

    internal void FixtureExecution(
        string document,
        TextSpan span,
        ExecutionCoverage state,
        CoverageReason[] reasons,
        CoverageMetric metric = CoverageMetric.Execution
    ) => _fixtureExecutions.Add((document, span, metric), (state, reasons.ToArray()));

    internal void AdvancementPoint(string document, TextSpan span, int methodToken) =>
        _advancements.Add((document, span, methodToken));

    internal CoverageEvidence InspectEnumeration(ICodeElement element)
    {
        var result = InspectAdvancement(element);
        return result with { Metric = CoverageMetric.Enumeration };
    }

    private CoverageEvidence InspectAdvancement(ICodeElement element)
    {
        var source = element.Source;
        if (
            element is CodeEnumeration loop
            && source is not null
            && _fixtureExecutions.TryGetValue(
                (source.Document.DocumentId, loop.Syntax.Span, CoverageMetric.Enumeration),
                out var fixture
            )
        )
            return Evidence(source, fixture.State, fixture.Reasons);
        if (
            source is null
            || !_functionRanges.TryGetValue(source.Document.DocumentId, out var ranges)
        )
            return Evidence(
                source,
                ExecutionCoverage.Unknown,
                [
                    source is null
                        ? CoverageReason.EvidenceNotPrepared
                        : MissingReason(source.Document.DocumentId),
                ]
            );
        if (
            element is not CodeEnumeration enumeration
            || !enumeration.IsResolved
            || enumeration.MoveNextMethod is null
        )
            return Evidence(
                source,
                ExecutionCoverage.Unknown,
                [CoverageReason.UnsupportedEnumerationMapping]
            );
        var points = _advancements
            .Where(point =>
                point.Document == source.Document.DocumentId
                && point.Span == enumeration.Syntax.InKeyword.Span
            )
            .ToArray();
        if (points.Length == 0)
            return Evidence(
                source,
                ExecutionCoverage.Unknown,
                [CoverageReason.UnsupportedEnumerationMapping]
            );
        var matching = ranges
            .Where(range =>
                points.Any(point =>
                    point.Span == range.Span && Token(range.FunctionIdentity) == point.Token
                )
            )
            .ToArray();
        if (
            points.Any(point =>
                !matching.Any(range => Token(range.FunctionIdentity) == point.Token)
            )
        )
            return Evidence(
                source,
                ExecutionCoverage.Unknown,
                [CoverageReason.MissingRange],
                matching
            );
        var state = MergePoints(matching);
        return Evidence(
            source,
            state,
            state == ExecutionCoverage.Unknown ? [CoverageReason.PartialRange] : [],
            matching
        );
    }

    private static int? Token(string identity) =>
        identity.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(
            identity.AsSpan(2),
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out var token
        )
            ? token
            : null;

    internal void FixtureLines(string document, LineCoverageMeasurement measurement) =>
        _fixtureLines.Add(document, measurement);

    internal void Unavailable(CoverageReason reason) => _unavailableReason = reason;

    internal void DocumentUnavailable(string documentId, CoverageReason reason) =>
        _documentReasons[documentId] = reason;

    private CoverageReason MissingReason(string documentId) =>
        _documentReasons.GetValueOrDefault(documentId, _unavailableReason);

    internal void Add(string documentId, IEnumerable<CoverageRange> ranges)
    {
        var functions = ranges
            .GroupBy(range => (range.Span, range.FunctionIdentity))
            .Select(group => new CoverageRange(
                group.Key.Span,
                MergePoints(group),
                group.Key.FunctionIdentity
            ))
            .ToArray();
        _functionRanges[documentId] = functions;
        _documents[documentId] = functions
            .GroupBy(range => range.Span)
            .Select(group => new CoverageRange(group.Key, MergePoints(group), ""))
            .ToArray();
    }

    private static ExecutionCoverage MergePoints(IEnumerable<CoverageRange> ranges) =>
        ranges.All(range => range.State == ExecutionCoverage.Covered) ? ExecutionCoverage.Covered
        : ranges.All(range => range.State == ExecutionCoverage.Uncovered)
            ? ExecutionCoverage.Uncovered
        : ExecutionCoverage.Unknown;

    internal ExecutionCoverage ExecutionOf(ICodeElement element) => Inspect(element).State;

    internal CoverageEvidence Inspect(ICodeElement element)
    {
        if (
            element.Source is { } fixtureSource
            && _fixtureExecutions.TryGetValue(
                (
                    fixtureSource.Document.DocumentId,
                    new(element.Location.Start, element.Location.Length),
                    CoverageMetric.Execution
                ),
                out var fixture
            )
        )
            return Evidence(fixtureSource, fixture.State, fixture.Reasons);
        if (
            element.Source is not { } source
            || !_documents.TryGetValue(source.Document.DocumentId, out var ranges)
        )
            return Evidence(
                element.Source,
                ExecutionCoverage.Unknown,
                [
                    element.Source is { } missing
                        ? MissingReason(missing.Document.DocumentId)
                        : CoverageReason.EvidenceNotPrepared,
                ]
            );
        var span = new TextSpan(element.Location.Start, element.Location.Length);
        if (_precise.TryGetValue((source.Document.DocumentId, span), out var precise))
            return precise;
        var matching = ranges.Where(range => range.Span.Contains(span)).ToArray();
        if (matching.Length == 0)
            return Evidence(source, ExecutionCoverage.Unknown, [CoverageReason.MissingRange]);
        if (matching.All(range => range.State == ExecutionCoverage.Uncovered))
            return Evidence(source, ExecutionCoverage.Uncovered, [], matching);
        if (matching.Any(range => range.State == ExecutionCoverage.Unknown))
            return Evidence(
                source,
                ExecutionCoverage.Unknown,
                [CoverageReason.PartialRange],
                matching
            );
        // Sequence points can cover both sides of a branch. A hit cannot identify its conditional arm.
        var node = source
            .Tree.GetRoot(source.Project.CancellationToken)
            .FindNode(span, getInnermostNodeForTie: true);
        foreach (var range in matching.Where(range => range.State == ExecutionCoverage.Covered))
        {
            if (range.Span == span)
                return Evidence(source, ExecutionCoverage.Covered, [], matching);
            var anchor = source.Tree.GetRoot().FindNode(range.Span, getInnermostNodeForTie: true);
            foreach (var expression in DirectExpressions(anchor))
            {
                if (expression.Span == span)
                    return Evidence(source, ExecutionCoverage.Covered, [], matching);
                // Member-reference selectors omit the invocation arguments; accept only an argument-free direct call.
                if (
                    expression is InvocationExpressionSyntax invocation
                    && invocation.Expression.Span == node.Span
                    && invocation.ArgumentList.Arguments.Count == 0
                )
                    return Evidence(source, ExecutionCoverage.Covered, [], matching);
            }
        }
        return Evidence(
            source,
            ExecutionCoverage.Unknown,
            [CoverageReason.UnsupportedExpressionMapping],
            matching
        );
    }

    private static CoverageEvidence Evidence(
        AnalysisSource? source,
        ExecutionCoverage state,
        CoverageReason[] reasons,
        CoverageRange[]? ranges = null
    ) =>
        new(
            state,
            Array.AsReadOnly(reasons),
            source?.Project.Name ?? "",
            source?.Project.TargetFramework ?? "",
            source?.Project.Snapshot.ContextId ?? ""
        )
        {
            Ranges = Array.AsReadOnly(
                (ranges ?? [])
                    .Select(range => new CoverageRangeEvidence(
                        source!.Document.DocumentId,
                        source.Document.Path,
                        range.Span.Start,
                        range.Span.Length,
                        range.State
                    ))
                    .ToArray()
            ),
        };

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

    internal LineCoverageMeasurement Measure(
        IEnumerable<AnalysisSource> sources,
        SourceLocation? location = null
    )
    {
        var covered = 0;
        var total = 0;
        var complete = true;
        var reasons = new HashSet<CoverageReason>();
        foreach (var source in sources)
        {
            if (
                location is null
                && _fixtureLines.TryGetValue(source.Document.DocumentId, out var fixture)
            )
            {
                covered += fixture.Covered;
                total += fixture.Coverable;
                complete &= fixture.IsComplete;
                reasons.UnionWith(
                    fixture.Reasons.Where(reason => reason != CoverageReason.ZeroCoverableLines)
                );
                continue;
            }
            if (!_documents.TryGetValue(source.Document.DocumentId, out var ranges))
            {
                complete = false;
                reasons.Add(MissingReason(source.Document.DocumentId));
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
        if (!complete)
            reasons.Add(CoverageReason.IncompleteLineEvidence);
        if (total == 0)
            reasons.Add(CoverageReason.ZeroCoverableLines);
        return new(covered, total, complete, Array.AsReadOnly(reasons.Order().ToArray()));
    }
}
