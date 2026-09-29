using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

/// <summary>Source-value argument relationships; nested computations are not mistaken for passing the referenced value itself.</summary>
public static class ArgumentUses
{
    /// <summary>Finds the direct containing invocation argument through parentheses and implicit non-user-defined conversions.</summary>
    public static CodeArgument? AsArgument(this MemberReference reference)
    {
        if (reference.Source is not { } source || reference.Syntax is not { } syntax)
            return null;
        var operation = source.Model.GetOperation(syntax, source.Project.CancellationToken);
        while (
            operation?.Parent
                is IConversionOperation { IsImplicit: true, Conversion.IsUserDefined: false }
                    or IParenthesizedOperation
        )
            operation = operation.Parent;
        if (
            operation?.Parent
            is not IArgumentOperation { Parent: IInvocationOperation parent } argument
        )
            return null;
        var call = new CodeInvocation(source, parent);
        return call.IsResolved
            ? call.Arguments.FirstOrDefault(value =>
                value.IsExplicit
                && value.Parameter.Ordinal
                    == argument.Parameter?.Ordinal + (call.Target.ReducedFrom is null ? 0 : 1)
                && value.Operation.Syntax.Span == argument.Value.Syntax.Span
            )
            : null;
    }

    /// <summary>Selects direct source argument uses associated with the specified declaration parameter.</summary>
    public static CodeQuery<CodeArgument> PassedAs(
        this CodeQuery<MemberReference> references,
        string parameter
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
        return references.SelectMany(reference =>
            reference.AsArgument() is { } argument && argument.Parameter.Name == parameter
                ? new[] { argument }
                : []
        );
    }
}
