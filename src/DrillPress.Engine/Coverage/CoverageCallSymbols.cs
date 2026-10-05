using System.IO.Abstractions;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress.Engine;

internal sealed class CoverageCallSymbols(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly CoverageCallSignature _signature = new();

    internal CoverageCallCompilation Read(
        AnalysisProject project,
        PEReader image,
        MetadataReader symbols
    )
    {
        var calls = project
            .Sources.Where(source => !source.Document.IsGenerated)
            .SelectMany(source =>
                source
                    .Tree.GetRoot(project.CancellationToken)
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Select(syntax => source.Model.GetOperation(syntax, project.CancellationToken))
                    .OfType<IInvocationOperation>()
                    .Select(operation => new CodeInvocation(source, operation))
            )
            .Where(call => call.IsResolved && !call.Operation.IsImplicit)
            .ToArray();
        if (calls.Length == 0)
            return new([], calls);
        var index = new CoverageCallIndex(calls);
        var result = new List<CoverageCallProof>();
        var metadata = image.GetMetadataReader();
        foreach (var handle in symbols.MethodDebugInformation)
        {
            project.CancellationToken.ThrowIfCancellationRequested();
            var method = MetadataTokens.MethodDefinitionHandle(MetadataTokens.GetRowNumber(handle));
            var definition = metadata.GetMethodDefinition(method);
            if (definition.RelativeVirtualAddress == 0)
                continue;
            var body = image.GetMethodBody(definition.RelativeVirtualAddress);
            var instructions = CoverageIL.Read(body);
            if (instructions is null || instructions.Length == 0)
                continue;
            var blocks = CoverageIL.Blocks(instructions, body);
            var debug = symbols.GetMethodDebugInformation(handle);
            var points = debug.GetSequencePoints().ToArray();
            var layout = ReadLayout(
                project,
                symbols,
                debug,
                points,
                blocks,
                body.GetILReader().Length
            );
            result.AddRange(
                ReadMethod(
                    index,
                    metadata,
                    MetadataTokens.GetToken(method),
                    body,
                    instructions,
                    blocks,
                    points,
                    layout
                )
            );
        }
        return new(result.ToArray(), calls);
    }

    private IEnumerable<CoverageCallProof> ReadMethod(
        CoverageCallIndex calls,
        MetadataReader metadata,
        int token,
        MethodBodyBlock body,
        CoverageInstruction[] instructions,
        int[] blocks,
        SequencePoint[] points,
        CoveragePointLayout?[] layout
    )
    {
        for (var index = 0; index < points.Length; index++)
        {
            if (layout[index] is not { } point)
                continue;
            var end =
                index + 1 < points.Length ? points[index + 1].Offset : body.GetILReader().Length;
            var candidates = calls.Within(point.DocumentId, point.Span).ToArray();
            foreach (var call in candidates)
            {
                if (
                    candidates.Count(other =>
                        SymbolEqualityComparer.Default.Equals(
                            CoverageCallSignature.Slot(other.Declaration),
                            CoverageCallSignature.Slot(call.Declaration)
                        )
                    ) != 1
                )
                    continue;
                var matching = instructions
                    .Where(instruction =>
                        instruction.Offset >= points[index].Offset
                        && instruction.Offset < end
                        && instruction.OpCode is ILOpCode.Call or ILOpCode.Callvirt
                        && _signature.Matches(metadata, instruction.Operand, call.Declaration)
                    )
                    .ToArray();
                if (matching.Length != 1)
                    continue;
                var selected = matching[0];
                var entry = CoverageIL.Ordinal(blocks, selected.Offset);
                var safe = CoverageCallPrefix.IsSafe(
                    instructions.Where(instruction =>
                        instruction.Offset >= blocks[entry] && instruction.Offset < selected.Offset
                    )
                );
                var completion = Completion(instructions, blocks, body, selected);
                yield return new(
                    call.Source.Document.DocumentId,
                    call.Operation.Syntax.Span,
                    point.Span,
                    token,
                    layout.OfType<CoveragePointLayout>().ToArray(),
                    entry,
                    safe,
                    completion,
                    selected.Offset
                );
            }
        }
    }

    private static int? Completion(
        CoverageInstruction[] instructions,
        int[] blocks,
        MethodBodyBlock body,
        CoverageInstruction selected
    )
    {
        if (
            selected.End >= body.GetILReader().Length
            || instructions.Any(instruction => instruction.Targets.Contains(selected.End))
            || body.ExceptionRegions.Any(region =>
                region.HandlerOffset == selected.End || region.FilterOffset == selected.End
            )
        )
            return null;
        var ordinal = Array.BinarySearch(blocks, selected.End);
        return ordinal >= 0 ? ordinal : null;
    }

    private CoveragePointLayout?[] ReadLayout(
        AnalysisProject project,
        MetadataReader symbols,
        MethodDebugInformation debug,
        SequencePoint[] points,
        int[] blocks,
        int methodEnd
    )
    {
        var result = new CoveragePointLayout?[points.Length];
        for (var index = 0; index < points.Length; index++)
        {
            var point = points[index];
            if (point.IsHidden)
                continue;
            var document = point.Document.IsNil ? debug.Document : point.Document;
            if (document.IsNil)
                continue;
            var path = symbols.GetString(symbols.GetDocument(document).Name);
            var source = project.Sources.SingleOrDefault(source =>
                SamePath(source.Document.Path, path)
            );
            if (source is null || Span(source, point) is not { } span)
                continue;
            var start = CoverageIL.Ordinal(blocks, point.Offset);
            var endOffset = index + 1 < points.Length ? points[index + 1].Offset : methodEnd;
            var upper =
                endOffset == methodEnd
                    ? blocks.Length
                    : Math.Max(
                        start + 1,
                        CoverageIL.Ordinal(blocks, endOffset)
                            + (
                                points[index + 1].IsHidden
                                && Array.BinarySearch(blocks, endOffset) < 0
                                    ? 1
                                    : 0
                            )
                    );
            if (start < 0 || upper <= start)
                continue;
            result[index] = new(
                source.Document.DocumentId,
                span,
                Enumerable.Range(start, upper - start).ToArray()
            );
        }
        return result;
    }

    private bool SamePath(string first, string second) =>
        string.Equals(
            _fileSystem.Path.GetFullPath(first),
            _fileSystem.Path.GetFullPath(second),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal
        );

    private static TextSpan? Span(AnalysisSource source, SequencePoint point)
    {
        var lines = source.Tree.GetText(source.Project.CancellationToken).Lines;
        if (
            point.StartLine < 1
            || point.EndLine < point.StartLine
            || point.EndLine > lines.Count
            || point.StartColumn < 1
            || point.EndColumn < 1
            || point.StartColumn > lines[point.StartLine - 1].Span.Length + 1
            || point.EndColumn > lines[point.EndLine - 1].Span.Length + 1
        )
            return null;
        var start = lines[point.StartLine - 1].Start + point.StartColumn - 1;
        var end = lines[point.EndLine - 1].Start + point.EndColumn - 1;
        return end > start ? TextSpan.FromBounds(start, end) : null;
    }
}
