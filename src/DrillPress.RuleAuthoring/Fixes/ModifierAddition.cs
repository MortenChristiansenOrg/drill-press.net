using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

/// <summary>An immutable single-token modifier addition with declaration checks and a behavior proof.</summary>
/// <remarks>Built-in gates: an editable ordinary declaration without directives or errors that lacks the modifier, the token inserted in conventional modifier order, corresponding declared symbols in every affected compilation and unchanged compiler-supplied arguments.</remarks>
public sealed class ModifierAddition
{
    // The conventional C# order; partial is always last.
    private static readonly SyntaxKind[] _order =
    [
        SyntaxKind.PublicKeyword,
        SyntaxKind.PrivateKeyword,
        SyntaxKind.ProtectedKeyword,
        SyntaxKind.InternalKeyword,
        SyntaxKind.FileKeyword,
        SyntaxKind.StaticKeyword,
        SyntaxKind.ExternKeyword,
        SyntaxKind.NewKeyword,
        SyntaxKind.VirtualKeyword,
        SyntaxKind.AbstractKeyword,
        SyntaxKind.SealedKeyword,
        SyntaxKind.OverrideKeyword,
        SyntaxKind.ReadOnlyKeyword,
        SyntaxKind.UnsafeKeyword,
        SyntaxKind.RequiredKeyword,
        SyntaxKind.VolatileKeyword,
        SyntaxKind.AsyncKeyword,
        SyntaxKind.PartialKeyword,
    ];

    private readonly AnalysisSource _source;
    private readonly SyntaxNode? _declaration;
    private readonly SyntaxKind _kind;
    private readonly (string Name, Func<ModifierChange, ProofResult> Prove)[] _checks;

    internal ModifierAddition(
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
    public ModifierAddition MustPreserve(DeclarationBehavior behavior) =>
        new(_source, _declaration, _kind, [.. _checks, .. BehaviorChecks.Declarations(behavior)]);

    /// <summary>Proposes <c>sealed</c> or <c>static</c> on a class with a library-owned proof over the loaded solution; other modifiers need <see cref="SafeWhen"/>.</summary>
    /// <remarks>
    /// <c>sealed</c>: no type in any loaded project derives from the class, and the class is not abstract or static.
    /// <c>static</c>: every member is static, no constructor is declared, the class derives from object, implements no interface, has no derived types, and no loaded project uses it except to access its members.
    /// Both assume that the loaded solution contains every assembly that derives from, instantiates or reflects over the type, including mocking proxies.
    /// </remarks>
    public FixProposal? Propose()
    {
        if (_kind is not (SyntaxKind.SealedKeyword or SyntaxKind.StaticKeyword))
            return FixProposal.Withheld(
                $"Propose() proves only sealed and static classes; adding {SyntaxFacts.GetText(_kind)} needs SafeWhen"
            );
        // Explain the solution-wide obstacle before compiling an edit that could only fail.
        if (
            _declaration is not null
            && _source.Model.GetDeclaredSymbol(_declaration, _source.Project.CancellationToken)
                is INamedTypeSymbol type
            && TypeUsage.Explain(_source.Project, type, _kind) is { } reason
        )
            return FixProposal.Withheld(reason);
        return SafeWhen(
            _kind == SyntaxKind.SealedKeyword ? SealedIsUnobservable : StaticIsUnobservable
        );
    }

    /// <summary>Proposes the addition when your proof holds in every affected compilation; false withholds the fix and keeps the finding.</summary>
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
        if (Array.IndexOf(_order, _kind) < 0)
            return FixProposal.Withheld(
                $"{SyntaxFacts.GetText(_kind)} cannot be added as a declaration modifier"
            );
        var modifiers = DeclarationSyntax.Modifiers(_declaration);
        if (modifiers.Any(_kind))
            return FixProposal.Withheld(
                $"the declaration already has {SyntaxFacts.GetText(_kind)}"
            );
        if (Anchor(_declaration) is not { RawKind: not 0 } anchor)
            return FixProposal.Withheld("the declaration has no place for a modifier");
        var edit = SourceChanges.Replace(
            _source,
            new TextSpan(anchor.SpanStart, 0),
            SyntaxFacts.GetText(_kind) + " "
        );
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

    // The new token goes before the first written modifier that conventionally follows it, or before the
    // declaration's keyword or type when none does.
    private SyntaxToken Anchor(SyntaxNode declaration)
    {
        var modifiers = DeclarationSyntax.Modifiers(declaration);
        var rank = Array.IndexOf(_order, _kind);
        foreach (var modifier in modifiers)
            if (
                Array.IndexOf(_order, (SyntaxKind)modifier.RawKind) is var other
                && (other > rank || other < 0)
            )
                return modifier;
        return modifiers.Count > 0
            ? modifiers.Last().GetNextToken()
            : declaration
                .DescendantTokens()
                .FirstOrDefault(token =>
                    !token.Parent!.AncestorsAndSelf().Any(node => node is AttributeListSyntax)
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
        var after = context
            .MapToken(source, Anchor(before))
            ?.Parent?.AncestorsAndSelf()
            .FirstOrDefault(node => node.RawKind == before.RawKind);
        if (after is null)
            return "the declaration has no counterpart after the edit";
        var added = DeclarationSyntax
            .Modifiers(after)
            .Where(token => token.IsKind(_kind))
            .ToArray();
        if (added.Length != 1)
            return "the added modifier is not written once in the edited declaration";
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
            || oldSymbols.Zip(newSymbols).Any(pair => !context.Same(pair.First, pair.Second))
        )
            return "the declared symbols do not correspond after the edit";
        var change = new ModifierChange(
            rewrite,
            default,
            Array.AsReadOnly(
                oldSymbols
                    .Zip(
                        newSymbols,
                        (oldSymbol, newSymbol) => new SymbolRewrite(oldSymbol, newSymbol)
                    )
                    .ToArray()
            ),
            added[0]
        );
        if (RewriteChecks.CompilerSuppliedArguments(rewrite, null) is { Holds: false } supplied)
            return supplied.Reason;
        foreach (var (name, prove) in _checks)
            if (prove(change) is var result && result != ProofResult.Proven)
                return $"the {name} check is {result.ToString().ToLowerInvariant()}";
        return proof(change) ? null : WithheldBy(change);
    }

    private string WithheldBy(ModifierChange change) =>
        _kind switch
        {
            SyntaxKind.SealedKeyword
            or SyntaxKind.StaticKeyword when change.Symbols[0].Before is INamedTypeSymbol type =>
                TypeUsage.Explain(change.Rewrite.Source.Project, type, _kind)
                    ?? "the rule's SafeWhen proof returned false",
            _ => "the rule's SafeWhen proof returned false",
        };

    private static bool SealedIsUnobservable(ModifierChange change) =>
        change.Symbols
            is [{ Before: INamedTypeSymbol type, After: INamedTypeSymbol { IsSealed: true } }]
        && TypeUsage.Explain(change.Rewrite.Source.Project, type, SyntaxKind.SealedKeyword) is null;

    private static bool StaticIsUnobservable(ModifierChange change) =>
        change.Symbols
            is [{ Before: INamedTypeSymbol type, After: INamedTypeSymbol { IsStatic: true } }]
        && TypeUsage.Explain(change.Rewrite.Source.Project, type, SyntaxKind.StaticKeyword) is null;
}
