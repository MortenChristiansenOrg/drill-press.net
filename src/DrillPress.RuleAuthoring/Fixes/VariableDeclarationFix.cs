namespace DrillPress;

/// <summary>A selected local, foreach or out variable declaration ready for a type edit.</summary>
public sealed class VariableDeclarationFix
{
    private readonly CodeVariableDeclaration _declaration;

    internal VariableDeclarationFix(CodeVariableDeclaration declaration) =>
        _declaration = declaration;

    /// <summary>Replaces only the written type with var. End with <see cref="VarConversion.Propose"/>.</summary>
    public VarConversion UseVar() => new(_declaration);
}
