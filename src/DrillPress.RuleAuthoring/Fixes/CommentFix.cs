namespace DrillPress;

/// <summary>A selected comment ready for removal.</summary>
public sealed class CommentFix
{
    private readonly CodeComment _comment;

    internal CommentFix(CodeComment comment) => _comment = comment;

    /// <summary>Deletes the comment, or its entire lines when it stands alone, retaining surrounding blank lines. End with <see cref="CommentRemoval.Propose"/>.</summary>
    public CommentRemoval Remove() => new(_comment);
}
