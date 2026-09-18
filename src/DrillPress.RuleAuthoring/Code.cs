using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>Root queries over a shared analysis; only selected projects supply candidates, and generated source is excluded.</summary>
public static class Code
{
    /// <summary>Selects resolved member expressions.</summary>
    public static CodeQuery<MemberReference> MemberReferences { get; } =
        new(
            (solution, names) =>
                solution
                    .SelectMemberReferences(names)
                    .Where(reference =>
                        reference.Source?.Project.Snapshot.IsAnalysisTarget != false
                    )
        );

    /// <summary>Selects ordinary source methods.</summary>
    public static CodeQuery<CodeMethod> Methods { get; } =
        new(solution =>
            solution.Methods.Where(method => method.Source.Project.Snapshot.IsAnalysisTarget)
        );

    /// <summary>Selects distinct ordinary named type definitions.</summary>
    public static CodeQuery<CodeDeclaration> Types { get; } =
        new(solution =>
            solution.Types.Where(type => type.Source.Project.Snapshot.IsAnalysisTarget)
        );

    /// <summary>Selects distinct interfaces, including partial and generic definitions.</summary>
    public static CodeQuery<CodeDeclaration> Interfaces { get; } =
        Types.Where(new(type => type.Symbol.TypeKind == TypeKind.Interface));
}
