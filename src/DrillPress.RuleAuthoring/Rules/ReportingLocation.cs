using Microsoft.CodeAnalysis;

namespace DrillPress;

internal static class ReportingLocation
{
    internal static SourceLocation For<T>(T candidate, object part)
    {
        var source =
            (candidate as ICodeElement)?.Source
            ?? throw new ArgumentException(
                "A source-part reporting selector requires a contextual candidate.",
                nameof(candidate)
            );
        if (part is ICodeElement element)
        {
            if (element.Source?.Project != source.Project)
                throw new ArgumentException(
                    "The reportable element belongs to another compilation context.",
                    nameof(part)
                );
            return Validate(source, element.Location);
        }
        if (part is SourceLocation location)
            return Validate(source, location);
        var tree = part switch
        {
            SyntaxNode node => node.SyntaxTree,
            SyntaxToken token => token.SyntaxTree,
            _ => null,
        };
        var member =
            source.Project.Sources.FirstOrDefault(candidateSource => candidateSource.Tree == tree)
            ?? throw new ArgumentException(
                "The selected syntax must belong to the candidate's original compilation.",
                nameof(part)
            );
        return part is SyntaxNode syntax
            ? member.Locate(syntax.Span)
            : member.Locate(((SyntaxToken)part).Span);
    }

    private static SourceLocation Validate(AnalysisSource source, SourceLocation location)
    {
        var target = source.Project.Sources.FirstOrDefault(candidate =>
            candidate.Document.Path == location.FilePath
        );
        if (
            target is null
            || location.Start < 0
            || location.Length < 0
            || (long)location.Start + location.Length > target.Tree.Length
        )
            throw new ArgumentException(
                "The reporting span must belong to an original document in the candidate's compilation.",
                nameof(location)
            );
        return location;
    }
}
