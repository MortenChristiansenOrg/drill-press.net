using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>Root queries over a shared analysis; generated source never becomes a candidate.</summary>
public static class Code
{
    /// <summary>Selects resolved member expressions.</summary>
    public static CodeQuery<MemberReference> MemberReferences { get; } = new(solution => solution.MemberReferences);

    /// <summary>Selects ordinary source methods.</summary>
    public static CodeQuery<CodeMethod> Methods { get; } = new(solution => solution.Methods);

    /// <summary>Selects distinct ordinary named type definitions.</summary>
    public static CodeQuery<CodeDeclaration> Types { get; } = new(solution => solution.Types);

    /// <summary>Selects distinct interfaces, including partial and generic definitions.</summary>
    public static CodeQuery<CodeDeclaration> Interfaces { get; } = Types.Where(new(type => type.Symbol.TypeKind == TypeKind.Interface));
}
