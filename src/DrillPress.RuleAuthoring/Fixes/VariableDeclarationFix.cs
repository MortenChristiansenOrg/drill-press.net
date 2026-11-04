namespace DrillPress;

/// <summary>A selected local, foreach or out variable declaration ready for a type edit or inlining.</summary>
public sealed class VariableDeclarationFix
{
    private readonly CodeVariableDeclaration _declaration;

    internal VariableDeclarationFix(CodeVariableDeclaration declaration) =>
        _declaration = declaration;

    /// <summary>Replaces only the written type with var. End with <see cref="VarConversion.Propose"/>.</summary>
    public VarConversion UseVar() => new(_declaration);

    /// <summary>Replaces the local's single read with its initializer and removes the declaration. End with <see cref="LocalInlining.Propose"/> or <see cref="LocalInlining.SafeWhen"/>.</summary>
    public LocalInlining Inline() => new(_declaration);
}
