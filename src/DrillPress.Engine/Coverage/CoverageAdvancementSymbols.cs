using System.IO.Abstractions;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress.Engine;

internal sealed class CoverageAdvancementSymbols(IFileSystem fileSystem)
{
    internal void Apply(AnalysisProject project, PEReader image, MetadataReader symbols)
    {
        var loops = project
            .Sources.Where(source => !source.Document.IsGenerated)
            .SelectMany(source =>
                source
                    .Tree.GetRoot(project.CancellationToken)
                    .DescendantNodes()
                    .OfType<CommonForEachStatementSyntax>()
                    .Select(syntax => new CodeEnumeration(source, syntax))
            )
            .Where(loop => loop.MoveNextMethod is not null)
            .ToArray();
        if (loops.Length == 0)
            return;
        var metadata = image.GetMetadataReader();
        foreach (var handle in symbols.MethodDebugInformation)
        {
            project.CancellationToken.ThrowIfCancellationRequested();
            ApplyMethod(project, loops, image, metadata, symbols, handle);
        }
    }

    private void ApplyMethod(
        AnalysisProject project,
        CodeEnumeration[] loops,
        PEReader image,
        MetadataReader metadata,
        MetadataReader symbols,
        MethodDebugInformationHandle handle
    )
    {
        var method = MetadataTokens.MethodDefinitionHandle(MetadataTokens.GetRowNumber(handle));
        var definition = metadata.GetMethodDefinition(method);
        if (definition.RelativeVirtualAddress == 0)
            return;
        var debug = symbols.GetMethodDebugInformation(handle);
        var points = debug.GetSequencePoints().ToArray();
        var body = image.GetMethodBody(definition.RelativeVirtualAddress);
        for (var index = 0; index < points.Length; index++)
        {
            var point = points[index];
            if (point.IsHidden)
                continue;
            var document = point.Document.IsNil ? debug.Document : point.Document;
            if (document.IsNil)
                continue;
            var path = symbols.GetString(symbols.GetDocument(document).Name);
            var end =
                index + 1 < points.Length ? points[index + 1].Offset : body.GetILReader().Length;
            foreach (var loop in loops.Where(loop => SamePath(loop.Source.Document.Path, path)))
            {
                if (
                    MatchesPoint(loop, point)
                    && CoverageAdvancementProof.Matches(
                        metadata,
                        body,
                        point.Offset,
                        end,
                        definition,
                        loop.MoveNextMethod!
                    )
                )
                    project.Coverage.AdvancementPoint(
                        loop.Source.Document.DocumentId,
                        loop.Syntax.InKeyword.Span,
                        MetadataTokens.GetToken(method)
                    );
            }
        }
    }

    private static bool MatchesPoint(CodeEnumeration loop, SequencePoint point)
    {
        var position = loop
            .Source.Tree.GetText(loop.Source.Project.CancellationToken)
            .Lines.GetLinePositionSpan(loop.Syntax.InKeyword.Span);
        return position.Start.Line + 1 == point.StartLine
            && position.End.Line + 1 == point.EndLine
            && position.Start.Character + 1 == point.StartColumn
            && position.End.Character + 1 == point.EndColumn;
    }

    private bool SamePath(string source, string symbols) =>
        string.Equals(
            fileSystem.Path.GetFullPath(source),
            fileSystem.Path.GetFullPath(symbols),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal
        );
}
