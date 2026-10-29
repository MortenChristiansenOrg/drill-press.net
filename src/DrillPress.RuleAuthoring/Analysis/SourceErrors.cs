using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

/// <summary>Checks a document once before querying individual spans in documents that contain errors.</summary>
internal sealed class SourceErrors
{
    private readonly AnalysisSource _source;
    private readonly Lazy<bool> _hasErrors;
    private readonly ConcurrentDictionary<TextSpan, bool> _members = new();

    internal SourceErrors(AnalysisSource source)
    {
        _source = source;
        _hasErrors = new(() =>
        {
            using var scope = source.Project.MeasureShared("errors");
            return source.Project.Errors.InDocument(source);
        });
    }

    internal bool In(TextSpan span)
    {
        _source.Project.CancellationToken.ThrowIfCancellationRequested();
        if (
            _source.Project.SolutionOrNull?.Options.EnableOptimizations != false
            && !_hasErrors.Value
        )
            return false;
        if (Member(span) is { } member && !_members.GetOrAdd(member, MemberHasErrors))
            return false;
        return Exactly(span);
    }

    private bool MemberHasErrors(TextSpan member)
    {
        using var scope = _source.Project.MeasureShared("errors");
        return Exactly(member);
    }

    private bool Exactly(TextSpan span) =>
        _source
            .Model.GetDiagnostics(span, _source.Project.CancellationToken)
            .Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    private TextSpan? Member(TextSpan span)
    {
        var root = _source.Tree.GetRoot(_source.Project.CancellationToken);
        if (!root.FullSpan.Contains(span))
            return null;
        for (
            var node = root.FindNode(span, getInnermostNodeForTie: true);
            node is not null;
            node = node.Parent
        )
        {
            if (
                node
                is BaseMethodDeclarationSyntax
                    or BasePropertyDeclarationSyntax
                    or BaseFieldDeclarationSyntax
                    or EnumMemberDeclarationSyntax
                    or GlobalStatementSyntax
            )
                return node.Span.Contains(span) ? node.Span : null;
            if (node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
                return null;
        }

        return null;
    }
}
