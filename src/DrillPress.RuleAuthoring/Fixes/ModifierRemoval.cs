using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

/// <summary>An immutable single-token edit with explicit declaration invariants and required behavior proof.</summary>
public sealed class ModifierRemoval
{
    private readonly AnalysisSource _source;
    private readonly SyntaxNode _declaration;
    private readonly SyntaxKind _kind;
    private readonly Func<RewriteEvidence, ProofResult>[] _contextChecks;
    private readonly Func<DeclarationRewrite, ProofResult>[] _checks;

    internal ModifierRemoval(
        AnalysisSource source,
        SyntaxNode declaration,
        SyntaxKind kind,
        Func<RewriteEvidence, ProofResult>[] contextChecks,
        Func<DeclarationRewrite, ProofResult>[]? checks = null
    )
    {
        _source = source;
        _declaration = declaration;
        _kind = kind;
        _contextChecks = contextChecks;
        _checks = checks ?? [];
    }

    /// <summary>Adds a declaration invariant without authorizing arbitrary behavior changes.</summary>
    public ModifierRemoval Require(Func<DeclarationRewrite, ProofResult> check) =>
        new(_source, _declaration, _kind, _contextChecks, [.. _checks, check]);

    /// <summary>Proposes the token removal. The consumer must prove its behavior in addition to configured identity/accessibility checks.</summary>
    public FixProposal? Propose(Func<DeclarationRewrite, ProofResult> provesBehavior)
    {
        if (
            _declaration.SyntaxTree != _source.Tree
            || !_source.Document.IsEditable
            || _source.Document.IsGenerated
            || _declaration.ContainsDirectives
            || _declaration.ContainsDiagnostics
        )
            return null;
        var tokens = DeclarationSyntax
            .Modifiers(_declaration)
            .Where(token => token.IsKind(_kind))
            .ToArray();
        if (tokens.Length != 1)
            return null;
        var token = tokens[0];
        var length =
            token.Span.Length
            + token
                .TrailingTrivia.TakeWhile(trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia))
                .Sum(trivia => trivia.FullSpan.Length);
        var edit = SourceChanges.Replace(_source, new TextSpan(token.SpanStart, length), "");
        return SourceChanges.Propose(
            [edit],
            context =>
            {
                var memberships = context
                    .Original.Sources.Where(source =>
                        source.Document.FileIdentity == _source.Document.FileIdentity
                    )
                    .ToArray();
                return memberships.Length > 0
                    && memberships.All(source => Validate(context, source, provesBehavior));
            }
        );
    }

    private bool Validate(
        RewriteContext context,
        AnalysisSource source,
        Func<DeclarationRewrite, ProofResult> provesBehavior
    )
    {
        var before = source
            .Tree.GetRoot(source.Project.CancellationToken)
            .FindNode(_declaration.Span, getInnermostNodeForTie: true);
        if (before.Span != _declaration.Span || before.RawKind != _declaration.RawKind)
            return false;
        var tokens = DeclarationSyntax
            .Modifiers(before)
            .Where(token => token.IsKind(_kind))
            .ToArray();
        if (tokens.Length != 1)
            return false;
        var anchor = before.DescendantTokens().FirstOrDefault(token => token != tokens[0]);
        var after = context
            .MapToken(source, anchor)
            ?.Parent?.AncestorsAndSelf()
            .FirstOrDefault(node => node.RawKind == before.RawKind);
        if (after is null)
            return false;
        var rewrite = new RewriteEvidence(
            context,
            new(source, before, after, context.Rewritten.GetSemanticModel(after.SyntaxTree)),
            []
        );
        var oldSymbols = DeclarationSyntax.Symbols(source.Model, before);
        var newSymbols = DeclarationSyntax.Symbols(rewrite.AfterModel, rewrite.After);
        if (
            tokens.Length != 1
            || oldSymbols is null
            || newSymbols is null
            || oldSymbols.Count != newSymbols.Count
            || oldSymbols.Count == 0
        )
            return false;
        var evidence = new DeclarationRewrite(
            rewrite,
            tokens[0],
            Array.AsReadOnly(
                oldSymbols
                    .Zip(
                        newSymbols,
                        (oldSymbol, newSymbol) => new SymbolRewrite(oldSymbol, newSymbol)
                    )
                    .ToArray()
            )
        );
        return RewriteChecks.SameCompilerSuppliedArguments(rewrite) == ProofResult.Proven
            && _contextChecks.All(check => check(rewrite) == ProofResult.Proven)
            && _checks.All(check => check(evidence) == ProofResult.Proven)
            && provesBehavior(evidence) == ProofResult.Proven;
    }
}
