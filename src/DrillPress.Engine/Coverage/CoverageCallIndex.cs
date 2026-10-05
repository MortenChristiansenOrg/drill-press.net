using Microsoft.CodeAnalysis.Text;

namespace DrillPress.Engine;

internal sealed class CoverageCallIndex(IEnumerable<CodeInvocation> calls)
{
    private readonly Dictionary<string, CodeInvocation[]> _documents = calls
        .GroupBy(call => call.Source.Document.DocumentId)
        .ToDictionary(
            group => group.Key,
            group => group.OrderBy(call => call.Operation.Syntax.Span.Start).ToArray()
        );

    internal IEnumerable<CodeInvocation> Within(string documentId, TextSpan span)
    {
        if (!_documents.TryGetValue(documentId, out var calls))
            yield break;
        var first = 0;
        var end = calls.Length;
        while (first < end)
        {
            var middle = first + (end - first) / 2;
            if (calls[middle].Operation.Syntax.Span.Start < span.Start)
                first = middle + 1;
            else
                end = middle;
        }
        for (var index = first; index < calls.Length; index++)
        {
            var call = calls[index];
            if (call.Operation.Syntax.Span.Start >= span.End)
                yield break;
            if (span.Contains(call.Operation.Syntax.Span))
                yield return call;
        }
    }
}
