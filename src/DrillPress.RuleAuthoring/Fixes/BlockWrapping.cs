using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

/// <summary>Wraps exactly one if/else embedded statement while preserving original syntax, branch ownership and bound references.</summary>
public sealed class BlockWrapping
{
    private readonly AnalysisSource _source;
    private readonly SyntaxNode _statement;
    private readonly Func<RewriteEvidence, ProofResult>[] _checks;

    internal BlockWrapping(
        AnalysisSource source,
        SyntaxNode statement,
        Func<RewriteEvidence, ProofResult>[] checks
    )
    {
        _source = source;
        _statement = statement;
        _checks = checks;
    }

    /// <summary>Adds a contextual invariant to the built-in restricted block-wrapping proof.</summary>
    public BlockWrapping Require(Func<RewriteEvidence, ProofResult> check) =>
        new(_source, _statement, [.. _checks, check]);

    /// <summary>Proposes two narrow boundary edits. Unsupported slots, labels, directives and ambiguous header trivia yield no proposal.</summary>
    public FixProposal? Propose()
    {
        if (
            _statement is not StatementSyntax statement
            || !Eligible(_source, statement)
            || Owner(statement) is not { } owner
        )
            return null;
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
                return null;
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
        return SourceChanges.Propose(
            edits,
            context =>
            {
                var sources = context
                    .Original.Sources.Where(source =>
                        source.Document.FileIdentity == _source.Document.FileIdentity
                    )
                    .ToArray();
                return sources.Length > 0 && sources.All(source => Validate(context, source));
            }
        );
    }

    private bool Validate(RewriteContext context, AnalysisSource source)
    {
        var before =
            source
                .Tree.GetRoot(source.Project.CancellationToken)
                .FindNode(_statement.Span, getInnermostNodeForTie: true) as StatementSyntax;
        if (
            before is null
            || before.Span != _statement.Span
            || !Eligible(source, before)
            || context.Evidence(source, before) is not { After: StatementSyntax after } evidence
            || after.Parent is not BlockSyntax { Statements.Count: 1 } block
            || block.Statements[0] != after
            || !SyntaxFactory.AreEquivalent(before, after)
            || !SameOwner(context, source, before, block)
        )
            return false;
        return RewriteChecks.SameSourceBindings(evidence) == ProofResult.Proven
            && RewriteChecks.SameCompilerSuppliedArguments(evidence) == ProofResult.Proven
            && _checks.All(check => check(evidence) == ProofResult.Proven);
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

    private static bool Eligible(AnalysisSource source, StatementSyntax statement)
    {
        if (
            statement.SyntaxTree != source.Tree
            || !source.Document.IsEditable
            || source.Document.IsGenerated
            || statement is BlockSyntax
            || statement.ContainsDiagnostics
            || statement.ContainsDirectives
            || statement
                .DescendantNodesAndSelf()
                .Any(node =>
                    node
                        is LabeledStatementSyntax
                            or GotoStatementSyntax
                            or YieldStatementSyntax
                            or LocalFunctionStatementSyntax
                )
        )
            return false;
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
        return headerEnd >= 0
            && source
                .Tree.GetText()
                .ToString(TextSpan.FromBounds(headerEnd, statement.SpanStart))
                .All(char.IsWhiteSpace);
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
