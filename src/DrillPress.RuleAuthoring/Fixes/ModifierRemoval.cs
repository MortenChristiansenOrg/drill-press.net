using DrillPress.Manifest;
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
    private readonly (string Name, Func<ModifierChange, ProofResult> Prove)[] _checks;

    internal ModifierRemoval(
        AnalysisSource source,
        SyntaxNode? declaration,
        SyntaxKind kind,
        (string Name, Func<ModifierChange, ProofResult> Prove)[]? checks = null
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
        !_accessibility.Contains(_kind)
            ? FixProposal.Withheld(
                $"Propose() proves only accessibility modifiers; removing {SyntaxFacts.GetText(_kind)} needs SafeWhen"
            )
            : MustPreserve(
                    DeclarationBehavior.Accessibility
                        | DeclarationBehavior.ContainingAccessibility
                        | DeclarationBehavior.Identity
                        | DeclarationBehavior.Contract
                )
                .SafeWhen(change => change.Removed.IsKind(_kind));

    /// <summary>Proposes the removal when your proof holds in every affected compilation; false withholds the fix and keeps the finding.</summary>
    public FixProposal? SafeWhen(Func<ModifierChange, bool> proof)
    {
        if (
            _declaration is null
            || _declaration.SyntaxTree != _source.Tree
            || !_source.Document.IsEditable
            || _source.Document.IsGenerated
        )
            return FixProposal.Withheld("the declaration is not editable ordinary source");
        if (_declaration.ContainsDirectives || _declaration.ContainsDiagnostics)
            return FixProposal.Withheld("the declaration contains directives or syntax errors");
        var tokens = DeclarationSyntax
            .Modifiers(_declaration)
            .Where(token => token.IsKind(_kind))
            .ToArray();
        if (tokens.Length != 1)
            return FixProposal.Withheld(
                $"the declaration has {tokens.Length} written {SyntaxFacts.GetText(_kind)} modifiers, not one"
            );
        var token = tokens[0];
        var length =
            token.Span.Length
            + token
                .TrailingTrivia.TakeWhile(trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia))
                .Sum(trivia => trivia.FullSpan.Length);
        var edit = SourceChanges.Replace(_source, new TextSpan(token.SpanStart, length), "");
        return SourceChanges.ProposeExplained(
            [edit],
            context =>
            {
                var memberships = context
                    .Original.Sources.Where(source =>
                        source.Document.FileIdentity == _source.Document.FileIdentity
                    )
                    .ToArray();
                if (memberships.Length == 0)
                    return "the edited file is not part of this compilation";
                foreach (var source in memberships)
                    if (Validate(context, source, proof, [edit]) is { } reason)
                        return reason;
                return null;
            }
        );
    }

    private string? Validate(
        RewriteContext context,
        AnalysisSource source,
        Func<ModifierChange, bool> proof,
        IReadOnlyList<SourceEdit> edits
    )
    {
        var before = source
            .Tree.GetRoot(source.Project.CancellationToken)
            .FindNode(_declaration!.Span, getInnermostNodeForTie: true);
        if (before.Span != _declaration.Span || before.RawKind != _declaration.RawKind)
            return "the declaration parses differently in this compilation";
        var tokens = DeclarationSyntax
            .Modifiers(before)
            .Where(token => token.IsKind(_kind))
            .ToArray();
        if (tokens.Length != 1)
            return "the modifier is not written once in this compilation";
        var anchor = before.DescendantTokens().FirstOrDefault(token => token != tokens[0]);
        var after = context
            .MapToken(source, anchor)
            ?.Parent?.AncestorsAndSelf()
            .FirstOrDefault(node => node.RawKind == before.RawKind);
        if (after is null)
            return "the declaration has no counterpart after the edit";
        var rewrite = new RewriteEvidence(
            context,
            new(source, before, after, context.Rewritten.GetSemanticModel(after.SyntaxTree)),
            [],
            edits
        );
        var oldSymbols = DeclarationSyntax.Symbols(source.Model, before);
        var newSymbols = DeclarationSyntax.Symbols(rewrite.AfterModel, rewrite.After);
        if (
            oldSymbols is null
            || newSymbols is null
            || oldSymbols.Count != newSymbols.Count
            || oldSymbols.Count == 0
        )
            return "the declared symbols do not correspond after the edit";
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
        if (RewriteChecks.CompilerSuppliedArguments(rewrite, null) is { Holds: false } supplied)
            return supplied.Reason;
        foreach (var (name, prove) in _checks)
            if (prove(change) is var result && result != ProofResult.Proven)
                return $"the {name} check is {result.ToString().ToLowerInvariant()}";
        return proof(change) ? null : "the rule's SafeWhen proof returned false";
    }
}
