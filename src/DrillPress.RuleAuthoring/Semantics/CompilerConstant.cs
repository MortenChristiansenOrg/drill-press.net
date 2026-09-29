using Microsoft.CodeAnalysis;

namespace DrillPress;

internal static class CompilerConstant
{
    internal static Optional<T> Read<T>(Optional<object?> constant, ITypeSymbol? type)
    {
        if (!constant.HasValue || type?.TypeKind == TypeKind.Error)
            return default;
        if (typeof(T).IsEnum)
            return
                type is { TypeKind: TypeKind.Enum }
                && CodeType.Of<T>().Matches(type)
                && constant.Value is { } value
                ? new((T)Enum.ToObject(typeof(T), value))
                : default;
        if (type?.TypeKind == TypeKind.Enum)
            return default;
        if (constant.Value is T typed)
            return new(typed);
        return constant.Value is null && default(T) is null ? new(default(T)!) : default;
    }

    internal static Optional<T> Read<T>(Optional<TypedConstant> constant) =>
        constant
            is {
                HasValue: true,
                Value.Kind: not (TypedConstantKind.Error or TypedConstantKind.Array)
            } value
            ? Read<T>(new Optional<object?>(value.Value.Value), value.Value.Type)
            : default;
}
