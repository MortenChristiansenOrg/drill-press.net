using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

/// <summary>Answers whether compiler errors fall in a span, compiling each enclosing member once instead of once per question.</summary>
/// <remarks>The errors reported for a span are a subset of those reported for any span containing it, so an error-free member
/// proves every span inside it error-free. A member with errors falls back to the exact span query.</remarks>
internal sealed class SourceErrors(AnalysisSource source)
{
    private readonly ConcurrentDictionary<TextSpan, bool> _members = new();

    internal bool In(TextSpan span)
    {
        if (Member(span) is { } member && !_members.GetOrAdd(member, Exactly))
            return false;
        return Exactly(span);
    }

    private bool Exactly(TextSpan span) =>
        source
            .Model.GetDiagnostics(span, source.Project.CancellationToken)
            .Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    private TextSpan? Member(TextSpan span)
    {
        var root = source.Tree.GetRoot(source.Project.CancellationToken);
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
