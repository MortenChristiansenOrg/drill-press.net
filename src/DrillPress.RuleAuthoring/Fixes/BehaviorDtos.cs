namespace DrillPress;

/// <summary>Bounded checks for an expression replacement's kept operands. No combination proves that two expressions mean the same thing.</summary>
[Flags]
public enum ExpressionBehavior
{
    /// <summary>Each kept operand is evaluated exactly once; does not establish order or conditional execution.</summary>
    EvaluationCounts = 1,

    /// <summary>Kept operands and other observable evaluations keep their straight-line order and count; branching or deferred shapes are unknown and withhold the fix.</summary>
    EvaluationOrder = 2,

    /// <summary>An instance receiver whose null check would move or disappear must be intrinsically non-null; nullable annotations alone are not runtime guarantees.</summary>
    NullReceiverBehavior = 4,
}

/// <summary>Bounded checks for a modifier edit. Equal identity and accessibility never prove arbitrary modifier changes safe.</summary>
[Flags]
public enum DeclarationBehavior
{
    /// <summary>The compiler's declared accessibility of every affected symbol.</summary>
    Accessibility = 1,

    /// <summary>The declared accessibility of the symbol and of each containing type.</summary>
    ContainingAccessibility = 2,

    /// <summary>The kind, containing declaration, name, signature and partial pairing of every affected symbol.</summary>
    Identity = 4,

    /// <summary>The compiler's static, abstract, virtual, override, sealed, extern, readonly and similar modifier facts.</summary>
    Contract = 8,
}
