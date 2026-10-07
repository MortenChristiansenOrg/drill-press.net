namespace DrillPress;

/// <summary>A selected if or else branch ready for a structural edit.</summary>
public sealed class BranchFix
{
    private readonly CodeBranch _branch;

    internal BranchFix(CodeBranch branch) => _branch = branch;

    /// <summary>Wraps the branch statement in braces. End with <see cref="BlockWrapping.Propose"/>.</summary>
    public BlockWrapping AddBraces() => new(_branch.Source, _branch.Syntax);
}
