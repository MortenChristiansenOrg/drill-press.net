namespace DrillPress;

/// <summary>The compiler's comment-trivia category; inactive preprocessor text is not a comment candidate.</summary>
public enum CodeCommentKind
{
    /// <summary>An ordinary comment extending from // to the end of a physical line.</summary>
    SingleLine,

    /// <summary>An ordinary comment delimited by /* and */.</summary>
    MultiLine,

    /// <summary>A structured /// or /** documentation comment.</summary>
    Documentation,
}
