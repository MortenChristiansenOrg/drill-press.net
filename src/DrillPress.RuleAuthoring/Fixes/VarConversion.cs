namespace DrillPress;

/// <summary>A var conversion whose safety the library proves: the inferred type, nullable annotations, tuple names and enclosing bindings are unchanged in every affected compilation.</summary>
public sealed class VarConversion
{
    private readonly CodeVariableDeclaration _declaration;

    internal VarConversion(CodeVariableDeclaration declaration) => _declaration = declaration;

    /// <summary>Proposes the conversion; target-typed, multi-variable, converted and unresolved declarations are withheld.</summary>
    public FixProposal? Propose() =>
        _declaration.Source.Document.IsEditable
        && !_declaration.Source.Document.IsGenerated
        && _declaration.CanUseVar
            ? VarRewrite.Propose(_declaration)
            : null;
}
