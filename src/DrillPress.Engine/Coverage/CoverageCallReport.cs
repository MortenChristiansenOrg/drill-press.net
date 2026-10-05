using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress.Engine;

internal sealed class CoverageCallReport
{
    internal void Apply(
        AnalysisProject project,
        XElement report,
        CoverageSymbolEvidence symbols,
        Dictionary<string, List<CoverageRange>> accepted,
        HashSet<string> invalid
    )
    {
        var modules = report
            .Descendants("module")
            .Where(module =>
                (string?)module.Attribute("id") == symbols.Identity
                && (string?)module.Attribute("name") == project.Snapshot.AssemblyName + ".dll"
            )
            .ToArray();
        if (modules.Length != 1)
            return;
        var module = modules[0];
        if (
            module.Element("block_data") is not { } data
            || (string?)data.Attribute("collector_version") != CoverageTool.Version
        )
            return;
        var buffer = Convert.FromBase64String(
            (string?)data.Attribute("buffer")
                ?? throw new InvalidDataException("Missing coverage block buffer.")
        );
        var sourceIds = module
            .Descendants("source_file")
            .ToDictionary(
                file => (string)file.Attribute("id")!,
                file => (string)file.Attribute("path")!
            );
        var methods = data.Elements("method").ToLookup(Token);
        var plans = symbols.Calls.ToLookup(plan => (plan.DocumentId, plan.Occurrence));
        var tokens = ReadTokens(data, sourceIds, symbols.Occurrences);
        foreach (
            var occurrence in symbols.Occurrences.Where(call =>
                accepted.ContainsKey(call.Source.Document.DocumentId)
                && !invalid.Contains(call.Source.Document.DocumentId)
            )
        )
            ApplyOccurrence(
                project,
                methods,
                buffer,
                sourceIds,
                occurrence,
                plans[(occurrence.Source.Document.DocumentId, occurrence.Operation.Syntax.Span)],
                tokens.GetValueOrDefault(
                    (occurrence.Source.Document.DocumentId, occurrence.Operation.Syntax.Span)
                ) ?? [],
                accepted[occurrence.Source.Document.DocumentId]
            );
    }

    private void ApplyOccurrence(
        AnalysisProject project,
        ILookup<int?, XElement> methods,
        byte[] buffer,
        Dictionary<string, string> sourceIds,
        CodeInvocation occurrence,
        IEnumerable<CoverageCallProof> plans,
        HashSet<int?> tokens,
        List<CoverageRange> ranges
    )
    {
        var span = occurrence.Operation.Syntax.Span;
        var relevant = ranges.Where(range => range.Span.Contains(span)).ToArray();
        var calls = plans
            .Select(plan =>
                ReadProof(methods[plan.Token].ToArray(), buffer, sourceIds, occurrence.Source, plan)
            )
            .OfType<CoverageCallEvidence>()
            .ToArray();
        var complete =
            calls.Length > 0 && tokens.All(token => calls.Any(call => call.MethodToken == token));
        var state =
            calls.Any(call => call.State == ExecutionCoverage.Covered) ? ExecutionCoverage.Covered
            : complete && calls.All(call => call.State == ExecutionCoverage.Uncovered)
                ? ExecutionCoverage.Uncovered
            : ExecutionCoverage.Unknown;
        CoverageReason[] reasons =
            state == ExecutionCoverage.Unknown
                ?
                [
                    relevant.Length == 0
                        ? CoverageReason.MissingRange
                        : CoverageReason.UnsupportedExpressionMapping,
                ]
                : [];
        project.Coverage.PreciseExecution(occurrence.Source, span, state, reasons, relevant, calls);
        if (occurrence.Operation.Syntax is InvocationExpressionSyntax syntax)
            project.Coverage.PreciseExecution(
                occurrence.Source,
                syntax.Expression.Span,
                state,
                reasons,
                relevant,
                calls
            );
    }

    private static Dictionary<(string DocumentId, TextSpan Span), HashSet<int?>> ReadTokens(
        XElement data,
        Dictionary<string, string> sourceIds,
        CodeInvocation[] occurrences
    )
    {
        var result = new Dictionary<(string, TextSpan), HashSet<int?>>();
        var index = new CoverageCallIndex(occurrences);
        var sources = occurrences
            .Select(call => call.Source)
            .DistinctBy(source => source.Document.DocumentId)
            .ToLookup(
                source => source.Document.Path,
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : null
            );
        foreach (var method in data.Elements("method"))
        foreach (var point in method.Elements("point"))
        {
            if (
                point.Attribute("source_id") is not { } id
                || !sourceIds.TryGetValue(id.Value, out var path)
            )
                continue;
            foreach (var source in sources[path])
            {
                if (Span(source, point) is not { } span)
                    continue;
                foreach (var call in index.Within(source.Document.DocumentId, span))
                {
                    var key = (source.Document.DocumentId, call.Operation.Syntax.Span);
                    if (!result.TryGetValue(key, out var tokens))
                        result.Add(key, tokens = []);
                    tokens.Add(Token(method));
                }
            }
        }
        return result;
    }

    private static CoverageCallEvidence? ReadProof(
        XElement[] methods,
        byte[] buffer,
        Dictionary<string, string> sourceIds,
        AnalysisSource source,
        CoverageCallProof plan
    )
    {
        if (methods.Length != 1)
            return null;
        var groups = methods[0]
            .Elements("point")
            .Where(point => MatchesPath(sourceIds, point, source))
            .Select(point => (Span: Span(source, point), Blocks: ReadBlocks(point, buffer)))
            .Where(point => point.Span is not null)
            .GroupBy(point => point.Span!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(point => point.Blocks).Distinct().Order().ToArray()
            );
        var verified = VerifyLayout(groups, source, plan);
        if (verified is not { } start || !groups.TryGetValue(plan.Point, out var reported))
            return null;
        var entry = start + plan.Entry;
        if (entry >= buffer.Length || !reported.Contains(entry))
            return null;
        var completion =
            plan.Completion is { } ordinal
            && start + ordinal < buffer.Length
            && groups.Values.Any(indexes => indexes.Contains(start + ordinal))
                ? start + ordinal
                : (int?)null;
        if (buffer[entry] == 0 && completion is { } next && buffer[next] != 0)
            return null;
        return new(
            source.Document.DocumentId,
            source.Document.Path,
            plan.Token,
            plan.InstructionOffset,
            entry,
            buffer[entry] != 0,
            plan.SafePrefix,
            completion,
            completion is { } index ? buffer[index] != 0 : null
        );
    }

    private static int? VerifyLayout(
        Dictionary<TextSpan, int[]> groups,
        AnalysisSource source,
        CoverageCallProof plan
    )
    {
        var layout = plan
            .Layout.Where(point => point.DocumentId == source.Document.DocumentId)
            .GroupBy(point => point.Span)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(point => point.Ordinals).Distinct().Order().ToArray()
            );
        if (
            !groups.TryGetValue(plan.Point, out var reported)
            || !layout.TryGetValue(plan.Point, out var expected)
            || reported.Length != expected.Length
            || reported.Length == 0
        )
            return null;
        var start = reported[0] - expected[0];
        if (
            start < 0
            || !reported.SequenceEqual(expected.Select(ordinal => start + ordinal))
            || groups.Any(group =>
                !layout.TryGetValue(group.Key, out var ordinals)
                || group.Value.Any(index => !ordinals.Contains(index - start))
            )
        )
            return null;
        return start;
    }

    private static int[] ReadBlocks(XElement point, byte[] buffer)
    {
        var result = new List<int>();
        foreach (var block in point.Elements("block"))
        {
            if (
                !int.TryParse((string?)block.Attribute("index"), out var index)
                || index < 0
                || index >= buffer.Length
                || (string?)block.Attribute("covered") != (buffer[index] == 0 ? "no" : "yes")
            )
                throw new InvalidDataException("Invalid coverage block identity or hit.");
            result.Add(index);
        }
        return result.ToArray();
    }

    private static bool MatchesPath(
        Dictionary<string, string> ids,
        XElement point,
        AnalysisSource source
    ) =>
        point.Attribute("source_id") is { } id
        && ids.TryGetValue(id.Value, out var path)
        && string.Equals(
            path,
            source.Document.Path,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal
        );

    private static int? Token(XElement method) =>
        int.TryParse(
            ((string?)method.Attribute("token"))?.Replace("0x", ""),
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out var token
        )
            ? token
            : null;

    private static TextSpan? Span(AnalysisSource source, XElement point)
    {
        var lines = source.Tree.GetText(source.Project.CancellationToken).Lines;
        if (
            !int.TryParse((string?)point.Attribute("start_line"), out var startLine)
            || !int.TryParse((string?)point.Attribute("end_line"), out var endLine)
            || !int.TryParse((string?)point.Attribute("start_column"), out var startColumn)
            || !int.TryParse((string?)point.Attribute("end_column"), out var endColumn)
            || startLine < 1
            || endLine < startLine
            || endLine > lines.Count
            || startColumn < 1
            || endColumn < 1
            || startColumn > lines[startLine - 1].Span.Length + 1
            || endColumn > lines[endLine - 1].Span.Length + 1
        )
            return null;
        var start = lines[startLine - 1].Start + startColumn - 1;
        var end = lines[endLine - 1].Start + endColumn - 1;
        return end > start ? TextSpan.FromBounds(start, end) : null;
    }
}
