using Microsoft.CodeAnalysis;

namespace DrillPress.Relationships;

/// <summary>Policy-neutral projections over the shared relationship graph and compiler override chains.</summary>
public static class RelationshipQueries
{
    /// <summary>Retains each maximal compatible view, including zero-entry views and alternative evaluations of the same framework.</summary>
    public static CodeQuery<ImplementationView> ImplementationViews(
        this CodeQuery<CodeDeclaration> owners
    ) =>
        owners.SelectMany(owner =>
            owner
                .Solution.Implementations.In(owner)
                .Select(view => new ImplementationView(owner, view.Projects, view.Implementations))
        );

    /// <summary>Filters entries inside each view without discarding empty views or changing their project compatibility graph.</summary>
    public static CodeQuery<ImplementationView> WhereImplementation(
        this CodeQuery<ImplementationView> views,
        Func<InterfaceImplementation, bool> predicate
    ) =>
        views.Select(view =>
            view with
            {
                Implementations = Array.AsReadOnly(view.Implementations.Where(predicate).ToArray()),
            }
        );

    /// <summary>Selects non-abstract classes and structs, including records. Does not assert public constructibility or DI activation.</summary>
    public static CodeQuery<ImplementationView> WhereConcrete(
        this CodeQuery<ImplementationView> views
    ) =>
        views.WhereImplementation(implementation =>
            !implementation.Symbol.IsAbstract
            && implementation.Symbol.TypeKind is TypeKind.Class or TypeKind.Struct
        );

    /// <summary>Projects every matching override edge, retaining its distance and constructed ancestor signature.</summary>
    public static CodeQuery<MethodOverride> OverrideMatches(
        this CodeQuery<CodeMethod> methods,
        CodeMember ancestor,
        OverrideSearch search = OverrideSearch.AnyAncestor
    )
    {
        if (!Enum.IsDefined(search))
            throw new ArgumentOutOfRangeException(nameof(search));
        return methods.SelectMany(method => Matches(method, ancestor, search));
    }

    /// <summary>Selects methods with a real compiler override relationship, never same-named hiding or interface implementations.</summary>
    public static CodeQuery<CodeMethod> Overriding(
        this CodeQuery<CodeMethod> methods,
        CodeMember ancestor,
        OverrideSearch search = OverrideSearch.AnyAncestor
    )
    {
        var matches = methods.OverrideMatches(ancestor, search);
        return CodeQuery<CodeMethod>.Create(solution =>
            matches.In(solution).Select(match => match.Method).Distinct()
        );
    }

    /// <summary>Selects a precise syntax body shape. Empty blocks do not imply effect-free invocation.</summary>
    public static CodeQuery<CodeMethod> WhereBody(
        this CodeQuery<CodeMethod> methods,
        MethodBodyShape shape
    )
    {
        if (!Enum.IsDefined(shape))
            throw new ArgumentOutOfRangeException(nameof(shape));
        return methods.Where(method => BodyShape(method) == shape);
    }

    /// <summary>Distinguishes missing bodies, empty/nonempty blocks and arrow expressions without interpreting policy.</summary>
    public static MethodBodyShape BodyShape(this CodeMethod method) =>
        method.Syntax.Body is { } block
            ? block.Statements.Count == 0
                ? MethodBodyShape.EmptyBlock
                : MethodBodyShape.NonEmptyBlock
            : method.Syntax.ExpressionBody is not null
                ? MethodBodyShape.Expression
                : MethodBodyShape.Missing;

    private static IEnumerable<MethodOverride> Matches(
        CodeMethod method,
        CodeMember ancestor,
        OverrideSearch search
    )
    {
        if (
            method
                .Source.Model.GetDiagnostics(
                    method.Syntax.Span,
                    method.Source.Project.CancellationToken
                )
                .Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
        )
            yield break;
        var distance = 0;
        for (
            var current = method.Symbol?.OverriddenMethod;
            current is not null;
            current = current.OverriddenMethod
        )
        {
            method.Source.Project.CancellationToken.ThrowIfCancellationRequested();
            distance++;
            if (current.ContainingType.TypeKind == TypeKind.Error)
                yield break;
            if (ancestor.Matches(current))
                yield return new(method, current, distance);
            if (search == OverrideSearch.Immediate)
                yield break;
        }
    }
}
