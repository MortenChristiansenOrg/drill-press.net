using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress;

/// <summary>Explicit C# declaration modifier tokens, independent of effective accessibility.</summary>
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

    /// <summary>File-scoped type accessibility.</summary>
    File = SyntaxKind.FileKeyword,

    /// <summary>Virtual member.</summary>
    Virtual = SyntaxKind.VirtualKeyword,

    /// <summary>Overridden member.</summary>
    Override = SyntaxKind.OverrideKeyword,
}
