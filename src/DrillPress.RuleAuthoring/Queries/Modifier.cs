using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress;

/// <summary>Written C# modifier tokens, independent of implicit defaults such as private members or internal top-level types.</summary>
public enum Modifier
{
    /// <summary>Explicit internal accessibility.</summary>
    Internal = SyntaxKind.InternalKeyword,

    /// <summary>Explicit public accessibility.</summary>
    Public = SyntaxKind.PublicKeyword,

    /// <summary>Explicit private accessibility.</summary>
    Private = SyntaxKind.PrivateKeyword,

    /// <summary>Explicit protected accessibility.</summary>
    Protected = SyntaxKind.ProtectedKeyword,

    /// <summary>File-scoped type accessibility.</summary>
    File = SyntaxKind.FileKeyword,

    /// <summary>Static declaration.</summary>
    Static = SyntaxKind.StaticKeyword,

    /// <summary>Abstract declaration.</summary>
    Abstract = SyntaxKind.AbstractKeyword,

    /// <summary>Sealed declaration.</summary>
    Sealed = SyntaxKind.SealedKeyword,

    /// <summary>Partial declaration.</summary>
    Partial = SyntaxKind.PartialKeyword,

    /// <summary>Readonly declaration.</summary>
    ReadOnly = SyntaxKind.ReadOnlyKeyword,

    /// <summary>Asynchronous method.</summary>
    Async = SyntaxKind.AsyncKeyword,

    /// <summary>Constant field or local.</summary>
    Const = SyntaxKind.ConstKeyword,

    /// <summary>Virtual member.</summary>
    Virtual = SyntaxKind.VirtualKeyword,

    /// <summary>Overriding member.</summary>
    Override = SyntaxKind.OverrideKeyword,

    /// <summary>Member hiding an inherited member.</summary>
    New = SyntaxKind.NewKeyword,

    /// <summary>Externally implemented member.</summary>
    Extern = SyntaxKind.ExternKeyword,

    /// <summary>Required member.</summary>
    Required = SyntaxKind.RequiredKeyword,

    /// <summary>Volatile field.</summary>
    Volatile = SyntaxKind.VolatileKeyword,

    /// <summary>Unsafe context.</summary>
    Unsafe = SyntaxKind.UnsafeKeyword,

    /// <summary>By-reference parameter, return or struct.</summary>
    Ref = SyntaxKind.RefKeyword,

    /// <summary>Output parameter.</summary>
    Out = SyntaxKind.OutKeyword,

    /// <summary>Read-only by-reference parameter.</summary>
    In = SyntaxKind.InKeyword,

    /// <summary>Parameter array or collection.</summary>
    Params = SyntaxKind.ParamsKeyword,

    /// <summary>Extension method receiver parameter.</summary>
    This = SyntaxKind.ThisKeyword,

    /// <summary>Scoped by-reference value.</summary>
    Scoped = SyntaxKind.ScopedKeyword,
}
