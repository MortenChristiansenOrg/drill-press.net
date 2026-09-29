using DrillPress;

namespace DrillPress;

/// <summary>Explicit string-template boundaries. Allowed calls retain their bindings; allowlisting does not establish purity or extraction safety.</summary>
public sealed class OneHoleTemplateOptions
{
    /// <summary>Configures supported forms, permitted local/parameter holes and exact API selectors for calls around the hole.</summary>
    public OneHoleTemplateOptions(
        TemplateShapes shapes,
        Func<CodeExpression, bool> allowedCapture,
        ApiSet? allowedCalls = null,
        int maximumNodes = 128
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumNodes, 1);
        if (
            shapes == 0
            || (shapes & ~(TemplateShapes.Interpolation | TemplateShapes.Concatenation)) != 0
        )
            throw new ArgumentOutOfRangeException(nameof(shapes));
        Shapes = shapes;
        AllowedCapture = allowedCapture;
        AllowedCalls = allowedCalls ?? new ApiSet();
        MaximumNodes = maximumNodes;
    }

    /// <summary>The explicitly selected syntax forms.</summary>
    public TemplateShapes Shapes { get; }

    /// <summary>The consumer's predicate on each bound candidate capture.</summary>
    public Func<CodeExpression, bool> AllowedCapture { get; }

    /// <summary>Approved identities for static calls whose arguments contain the single hole; their behavior still requires proof.</summary>
    public ApiSet AllowedCalls { get; }

    /// <summary>A finite bound on expression syntax inspected per candidate.</summary>
    public int MaximumNodes { get; }
}
