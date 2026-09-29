using DrillPress.Operations;

namespace DrillPress.Queries;

internal sealed class CandidateComparer<T> : IEqualityComparer<T>
{
    internal static CandidateComparer<T> Instance { get; } = new();

    public bool Equals(T? first, T? second) =>
        first is CodeArgument { Location: null } leftArgument
        && second is CodeArgument { Location: null } rightArgument
            ? leftArgument.Source == rightArgument.Source
                && leftArgument.Invocation.Location == rightArgument.Invocation.Location
                && leftArgument.Parameter.Ordinal == rightArgument.Parameter.Ordinal
        : first is CodeArgument { Location: null } || second is CodeArgument { Location: null }
            ? false
        : first is ICodeElement { Source: { } left } a
        && second is ICodeElement { Source: { } right } b
            ? left == right && a.Location == b.Location
        : EqualityComparer<T>.Default.Equals(first, second);

    public int GetHashCode(T value) =>
        value is CodeArgument { Location: null } argument
            ? HashCode.Combine(
                argument.Source,
                argument.Invocation.Location,
                argument.Parameter.Ordinal
            )
        : value is ICodeElement { Source: { } source } element
            ? HashCode.Combine(source, element.Location)
        : value is null ? 0
        : EqualityComparer<T>.Default.GetHashCode(value);
}
