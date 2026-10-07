using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

/// <summary>An immutable single-token modifier removal with declaration checks and a required behavior proof.</summary>
/// <remarks>Built-in gates: exactly one matching token in an editable ordinary declaration without directives or errors, matching declared symbols in every affected compilation and unchanged compiler-supplied arguments. Behavior checks are added with <see cref="MustPreserve"/>.</remarks>
public sealed class ModifierRemoval
{
    private static readonly SyntaxKind[] _accessibility =
    [
        SyntaxKind.PublicKeyword,
        SyntaxKind.PrivateKeyword,
        SyntaxKind.ProtectedKeyword,
        SyntaxKind.InternalKeyword,
    ];

    private readonly AnalysisSource _source;
    private readonly SyntaxNode? _declaration;
    private readonly SyntaxKind _kind;
    private readonly Func<ModifierChange, ProofResult>[] _checks;

    internal ModifierRemoval(
        AnalysisSource source,
        SyntaxNode? declaration,
        SyntaxKind kind,
        Func<ModifierChange, ProofResult>[]? checks = null
    )
    {
        _source = source;
        _declaration = declaration;
        _kind = kind;
        _checks = checks ?? [];
    }

    /// <summary>Adds bounded declaration checks such as unchanged declared accessibility and identity. They do not prove arbitrary modifier changes safe.</summary>
    public ModifierRemoval MustPreserve(DeclarationBehavior behavior) =>
        new(_source, _declaration, _kind, [.. _checks, .. BehaviorChecks.Declarations(behavior)]);

    /// <summary>Proposes removing an accessibility modifier when every affected symbol keeps its declared accessibility, identity and contract, such as an explicit <c>internal</c> on a top-level type or <c>private</c> on a member. Other modifiers need <see cref="SafeWhen"/>.</summary>
    public FixProposal? Propose() =>
        _accessibility.Contains(_kind)
            ? MustPreserve(
                    DeclarationBehavior.Accessibility
                        | DeclarationBehavior.ContainingAccessibility
                        | DeclarationBehavior.Identity
                        | DeclarationBehavior.Contract
                )
                .SafeWhen(change => change.Removed.IsKind(_kind))
            : null;

    /// <summary>Proposes the removal when your proof holds in every affected compilation; false withholds the fix and keeps the finding.</summary>
    public FixProposal? SafeWhen(Func<ModifierChange, bool> proof)
    {
        if (
            _declaration is null
            || _declaration.SyntaxTree != _source.Tree
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
                    && memberships.All(source => Validate(context, source, proof));
            }
        );
    }

    private bool Validate(
        RewriteContext context,
        AnalysisSource source,
        Func<ModifierChange, bool> proof
    )
    {
        var before = source
            .Tree.GetRoot(source.Project.CancellationToken)
            .FindNode(_declaration!.Span, getInnermostNodeForTie: true);
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
            oldSymbols is null
            || newSymbols is null
            || oldSymbols.Count != newSymbols.Count
            || oldSymbols.Count == 0
        )
            return false;
        var change = new ModifierChange(
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
            && _checks.All(check => check(change) == ProofResult.Proven)
            && proof(change);
    }
}
