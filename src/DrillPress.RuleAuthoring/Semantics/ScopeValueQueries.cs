using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>Finds the values code can use at an element, such as the <c>CancellationToken</c> or <c>TimeProvider</c> to pass on.</summary>
public static class ScopeValueQueries
{
    /// <summary>
    /// The locals, parameters, fields and properties of a type (or a derived or implementing type) that code at the element's
    /// location can read by name, nearest first: locals declared before it (latest first), parameters of the enclosing functions
    /// (innermost first) and of the primary constructor, then fields and properties of the containing types by name.
    /// </summary>
    /// <remarks>Instance members and primary constructor parameters are left out in a static context. A local in its own
    /// initializer and locals declared later are not usable and are left out. Values reachable only through another value, such
    /// as <c>options.Clock</c>, are not included.</remarks>
    public static IReadOnlyList<ScopedValue> ValuesInScope(this ICodeElement element, CodeType type)
    {
        var source = element.Source;
        var position = element.Location.Start;
        var token = source.Project.CancellationToken;
        var functions = EnclosingFunctions(source.Model.GetEnclosingSymbol(position, token));
        var isStatic =
            functions.Count > 0 ? IsStaticContext(functions) : IsStaticMember(source, position);
        var values = source
            .Model.LookupSymbols(position)
            .Select(symbol => Describe(symbol, position, isStatic, functions))
            .OfType<(ScopedValue Value, int Group, int Order, string Name)>()
            .Where(item => IsOrDerivesFrom(item.Value.Type, type))
            .OrderBy(item => item.Group)
            .ThenBy(item => item.Order)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .Select(item => item.Value)
            .ToArray();
        return Array.AsReadOnly(values);
    }

    /// <summary>The values of type <typeparamref name="T"/> (or a derived or implementing type) that code at the element can read by name, nearest first.</summary>
    public static IReadOnlyList<ScopedValue> ValuesInScope<T>(this ICodeElement element) =>
        element.ValuesInScope(CodeType.Of<T>());

    private static (ScopedValue, int, int, string)? Describe(
        ISymbol symbol,
        int position,
        bool isStatic,
        IReadOnlyList<IMethodSymbol> functions
    ) =>
        symbol switch
        {
            ILocalSymbol local when IsDeclaredBefore(local, position) => (
                new(local, ScopedValueKind.Local, local.Type),
                0,
                -local.Locations[0].SourceSpan.Start,
                local.Name
            ),
            IParameterSymbol parameter
                when FunctionIndex(functions, parameter) is var index
                    && (index >= 0 || !isStatic) => (
                new(parameter, ScopedValueKind.Parameter, parameter.Type),
                1,
                (index >= 0 ? index : functions.Count) * 1000 + parameter.Ordinal,
                parameter.Name
            ),
            IFieldSymbol { IsImplicitlyDeclared: false } field when field.IsStatic || !isStatic => (
                new(field, ScopedValueKind.Field, field.Type),
                2,
                0,
                field.Name
            ),
            IPropertySymbol { IsIndexer: false } property when property.IsStatic || !isStatic => (
                new(property, ScopedValueKind.Property, property.Type),
                2,
                0,
                property.Name
            ),
            _ => null,
        };

    private static bool IsDeclaredBefore(ILocalSymbol local, int position) =>
        local.Locations is [{ IsInSource: true } location, ..]
        && location.SourceSpan.End <= position
        && !local.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax() is VariableDeclaratorSyntax declarator
            && declarator.Span.Contains(position)
        );

    private static int FunctionIndex(
        IReadOnlyList<IMethodSymbol> functions,
        IParameterSymbol parameter
    )
    {
        for (var index = 0; index < functions.Count; index++)
            if (SymbolEqualityComparer.Default.Equals(functions[index], parameter.ContainingSymbol))
                return index;
        return -1;
    }

    // The functions around a position, innermost first, up to the member that contains them.
    private static IReadOnlyList<IMethodSymbol> EnclosingFunctions(ISymbol? symbol)
    {
        var functions = new List<IMethodSymbol>();
        for (
            var current = symbol as IMethodSymbol;
            current is not null;
            current = current.ContainingSymbol as IMethodSymbol
        )
        {
            functions.Add(current);
            if (
                current.MethodKind is not (MethodKind.AnonymousFunction or MethodKind.LocalFunction)
            )
                break;
        }
        return functions;
    }

    // A static lambda or local function, or any function in a static member, cannot read instance state.
    private static bool IsStaticContext(IReadOnlyList<IMethodSymbol> functions) =>
        functions.Any(function => function.IsStatic);

    private static bool IsStaticMember(AnalysisSource source, int position) =>
        source.Model.GetEnclosingSymbol(position, source.Project.CancellationToken)
            is { IsStatic: true };

    private static bool IsOrDerivesFrom(ITypeSymbol type, CodeType target) =>
        target.Matches(type)
        || type is INamedTypeSymbol named
            && (named.IsOrDerivesFrom(target) || Symbols.Implements(named, target))
        || type is ITypeParameterSymbol parameter
            && parameter.ConstraintTypes.Any(constraint => IsOrDerivesFrom(constraint, target));
}
