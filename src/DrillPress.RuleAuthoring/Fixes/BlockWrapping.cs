using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

/// <summary>Adds braces around exactly one if or else embedded statement while preserving its syntax, branch ownership and bound references.</summary>
public sealed class BlockWrapping
{
    private readonly AnalysisSource _source;
    private readonly SyntaxNode _statement;

    internal BlockWrapping(AnalysisSource source, SyntaxNode statement)
    {
        _source = source;
        _statement = statement;
    }

    /// <summary>Proposes the two brace insertions, using the file's indentation and line endings. The library owns this restricted equivalence proof; labels, directives and ambiguous header comments yield no proposal.</summary>
    /// <remarks>Gates: editable ordinary source, a supported if/else branch, safe boundary trivia, and unchanged branch ownership, bound expressions and compiler-supplied arguments in every affected compilation.</remarks>
    public FixProposal? Propose()
    {
        if (_statement is not StatementSyntax statement)
            return FixProposal.Withheld("the branch is not a statement");
        if (Ineligible(_source, statement) is { } ineligible)
            return FixProposal.Withheld(ineligible);
        if (Owner(statement) is not { } owner)
            return FixProposal.Withheld("the statement is not an if or else branch");
        var text = _source.Tree.GetText();
        var line = text.Lines.GetLineFromPosition(statement.SpanStart);
        var prefix = text.ToString(TextSpan.FromBounds(line.Start, statement.SpanStart));
        var layout = BlockLayout.Read(text, owner);
        var start = prefix.All(char.IsWhiteSpace) ? line.Start : statement.SpanStart;
        var opening =
            (start == line.Start ? layout.Indent : "")
            + "{"
            + layout.NewLine
            + layout.Indent
            + layout.Unit;
        var end = statement.Span.End;
        foreach (var trivia in statement.GetTrailingTrivia())
        {
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                break;
            if (
                trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
            )
                end = trivia.Span.End;
            else if (!trivia.IsKind(SyntaxKind.WhitespaceTrivia))
                return FixProposal.Withheld(
                    "the statement is followed by trivia that cannot stay on its line"
                );
        }
        var edits = new[]
        {
            SourceChanges.Replace(
                _source,
                TextSpan.FromBounds(start, statement.SpanStart),
                opening
            ),
            SourceChanges.Replace(
                _source,
                new TextSpan(end, 0),
                layout.NewLine + layout.Indent + "}"
            ),
        };
        return SourceChanges.ProposeExplained(
            edits,
            context =>
            {
                var sources = context
                    .Original.Sources.Where(source =>
                        source.Document.FileIdentity == _source.Document.FileIdentity
                    )
                    .ToArray();
                if (sources.Length == 0)
                    return "the edited file is not part of this compilation";
                foreach (var source in sources)
                    if (Validate(context, source, edits) is { } reason)
                        return reason;
                return null;
            }
        );
    }

    private string? Validate(RewriteContext context, AnalysisSource source, SourceEdit[] edits)
    {
        var before =
            source
                .Tree.GetRoot(source.Project.CancellationToken)
                .FindNode(_statement.Span, getInnermostNodeForTie: true) as StatementSyntax;
        if (before is null || before.Span != _statement.Span)
            return "the statement parses differently in this compilation";
        if (Ineligible(source, before) is { } ineligible)
            return ineligible;
        if (
            context.Evidence(source, before, edits) is not { After: StatementSyntax after } evidence
            || after.Parent is not BlockSyntax { Statements.Count: 1 } block
            || block.Statements[0] != after
            || !SyntaxFactory.AreEquivalent(before, after)
        )
            return "the statement is not the only, unchanged statement of the new block";
        if (!SameOwner(context, source, before, block))
            return "the new block does not belong to the same statement";
        if (RewriteChecks.SourceBindings(evidence) is { Holds: false } bindings)
            return bindings.Reason;
        return RewriteChecks.CompilerSuppliedArguments(evidence, null).Reason;
    }

    private static bool SameOwner(
        RewriteContext context,
        AnalysisSource source,
        StatementSyntax before,
        BlockSyntax block
    ) =>
        before.Parent switch
        {
            IfStatementSyntax owner => context.MapToken(source, owner.IfKeyword)?.Parent
                is IfStatementSyntax mapped
                && mapped.Statement == block,
            ElseClauseSyntax clause => clause.Parent is IfStatementSyntax oldOwner
                && context.MapToken(source, oldOwner.IfKeyword)?.Parent is IfStatementSyntax mapped
                && mapped.Else?.Statement == block,
            _ => false,
        };

    private static string? Ineligible(AnalysisSource source, StatementSyntax statement)
    {
        if (
            statement.SyntaxTree != source.Tree
            || !source.Document.IsEditable
            || source.Document.IsGenerated
        )
            return "the statement is in generated or non-editable source";
        if (statement is BlockSyntax)
            return "the statement already has braces";
        if (statement.ContainsDiagnostics || statement.ContainsDirectives)
            return "the statement contains directives or syntax errors";
        if (
            statement
                .DescendantNodesAndSelf()
                .Any(node =>
                    node
                        is LabeledStatementSyntax
                            or GotoStatementSyntax
                            or YieldStatementSyntax
                            or LocalFunctionStatementSyntax
                )
        )
            return "the statement contains a label, goto, yield or local function";
        var headerEnd = statement.Parent switch
        {
            IfStatementSyntax owner when owner.Statement == statement => owner
                .CloseParenToken
                .Span
                .End,
            ElseClauseSyntax clause when clause.Statement == statement => clause
                .ElseKeyword
                .Span
                .End,
            _ => -1,
        };
        if (headerEnd < 0)
            return "the statement is not an if or else branch";
        return source
            .Tree.GetText()
            .ToString(TextSpan.FromBounds(headerEnd, statement.SpanStart))
            .All(char.IsWhiteSpace)
            ? null
            : "a comment between the header and the statement has no unambiguous place";
    }

    private static IfStatementSyntax? Owner(StatementSyntax statement)
    {
        var owner =
            statement.Parent as IfStatementSyntax ?? statement.Parent?.Parent as IfStatementSyntax;
        while (owner?.Parent is ElseClauseSyntax { Parent: IfStatementSyntax parent })
            owner = parent;
        return owner;
    }
}
