using Microsoft.CodeAnalysis;

namespace DrillPress.SampleRules.Relationships;

internal static class InterfacePolicy
{
    internal static RuleCondition<CodeDeclaration> HasSingleProductionImplementation { get; } =
        new(type =>
            type.Solution.Implementations.In(type)
                .Any(view =>
                    view.Implementations.Count(implementation =>
                        !implementation.Project.IsTestProject
                        && !implementation.Symbol.IsAbstract
                        && implementation.Symbol.TypeKind is TypeKind.Class or TypeKind.Struct
                    ) == 1
                )
        );
}
