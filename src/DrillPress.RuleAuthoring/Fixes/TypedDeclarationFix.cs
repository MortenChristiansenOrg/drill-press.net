namespace DrillPress;

/// <summary>Corrections for a written declaration type, validated in every affected compilation context.</summary>
public sealed class TypedDeclarationFix
{
    private readonly CodeTypedDeclaration _declaration;

    internal TypedDeclarationFix(CodeTypedDeclaration declaration) => _declaration = declaration;

    /// <summary>Replaces only the written type span with var, preserving exterior trivia. Withholds unsafe, unresolved and target-typed declarations.</summary>
    public FixProposal? UseVar() =>
        _declaration.Source.Document.IsEditable
        && !_declaration.Source.Document.IsGenerated
        && _declaration.CanUseVar
            ? VarRewrite.Propose(_declaration)
            : null;
}
