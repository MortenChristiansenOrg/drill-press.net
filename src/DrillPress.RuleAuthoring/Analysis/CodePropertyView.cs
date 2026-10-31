using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>One property as seen through a selected type, including synthesized positional record properties. Each distinct generic substitution has its own view, reported at the source that declares the property.</summary>
public sealed class CodePropertyView : ICodeDeclaration
{
    internal CodePropertyView(
        AnalysisSource source,
        SyntaxNode syntax,
        IPropertySymbol symbol,
        PropertyDefault? defaultValue,
        IReadOnlyList<TypeReachability> reachedFrom
    )
    {
        Source = source;
        Syntax = syntax;
        Symbol = symbol;
        Default = defaultValue;
        ReachedFrom = reachedFrom;
        (Origin, var anchor) = syntax switch
        {
            ParameterSyntax parameter => (
                PropertyViewOrigin.PositionalParameter,
                parameter.Identifier
            ),
            IndexerDeclarationSyntax indexer => (PropertyViewOrigin.Indexer, indexer.ThisKeyword),
            PropertyDeclarationSyntax property => (
                PropertyViewOrigin.PropertyDeclaration,
                property.Identifier
            ),
            _ => throw new ArgumentException(
                "A property view requires a property, indexer or positional parameter.",
                nameof(syntax)
            ),
        };
        Location = source.Locate(anchor.Span);
    }

    /// <summary>The document and compilation membership of the declaring source.</summary>
    public AnalysisSource Source { get; }

    /// <summary>The written property, indexer or positional parameter.</summary>
    public SyntaxNode Syntax { get; }

    /// <summary>The property with generic substitutions applied.</summary>
    public IPropertySymbol Symbol { get; }

    ISymbol? ICodeDeclaration.Symbol => Symbol;

    /// <summary>The source declaration's identity, without substitutions.</summary>
    public IPropertySymbol Definition => Symbol.OriginalDefinition;

    /// <summary>The written declaration this view is anchored to.</summary>
    public PropertyViewOrigin Origin { get; }

    /// <summary>The written property, or null for a positional parameter or indexer.</summary>
    public CodeProperty? Declaration =>
        Syntax is PropertyDeclarationSyntax property ? new(Source, property) : null;

    /// <summary>The positional record parameter, or null for an explicit declaration.</summary>
    public CodeParameter? PositionalParameter =>
        Syntax is ParameterSyntax parameter ? new(Source, parameter) : null;

    /// <summary>The written initializer or positional parameter default, if any.</summary>
    public PropertyDefault? Default { get; }

    /// <summary>The reachability results that selected this property, in query order, including their seeds and completeness. Empty when the view started from type definitions.</summary>
    public IReadOnlyList<TypeReachability> ReachedFrom { get; }

    /// <summary>The declared name.</summary>
    public string Name => Symbol.Name;

    /// <summary>Identifier words preserving casing, including acronym and digit boundaries.</summary>
    public IReadOnlyList<string> NameWords => CodeIdentifier.NamedWords(Name);

    /// <summary>The compiler's declared accessibility.</summary>
    public Accessibility Accessibility => Symbol.DeclaredAccessibility;

    /// <summary>The property type with generic substitutions applied.</summary>
    public ITypeSymbol Type => Symbol.Type;

    /// <summary>The substituted type that declares the property, which is a base type for an inherited property.</summary>
    public INamedTypeSymbol DeclaringType => Symbol.ContainingType;

    /// <summary>Whether generic substitutions apply, so that this view differs from its definition.</summary>
    public bool IsConstructed => !SymbolEqualityComparer.Default.Equals(Symbol, Definition);

    /// <summary>The declared nullability after generic substitution.</summary>
    public PropertyNullability Nullability =>
        Type switch
        {
            { TypeKind: TypeKind.Error } => PropertyNullability.Unavailable,
            { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } =>
                PropertyNullability.Nullable,
            { IsValueType: true } => PropertyNullability.NonNullable,
            _ when Symbol.NullableAnnotation == NullableAnnotation.Annotated =>
                PropertyNullability.Nullable,
            _ when Symbol.NullableAnnotation == NullableAnnotation.None =>
                PropertyNullability.Oblivious,
            ITypeParameterSymbol { HasNotNullConstraint: true } => PropertyNullability.NonNullable,
            ITypeParameterSymbol
            {
                HasReferenceTypeConstraint: true,
                ReferenceTypeConstraintNullableAnnotation: NullableAnnotation.NotAnnotated
            } => PropertyNullability.NonNullable,
            ITypeParameterSymbol => PropertyNullability.TypeParameter,
            _ => PropertyNullability.NonNullable,
        };

    /// <summary>Whether the property has a getter.</summary>
    public bool HasGetter => Accessor(Symbol, property => property.GetMethod) is not null;

    /// <summary>Whether the property has a set accessor.</summary>
    public bool HasSetter =>
        Accessor(Symbol, property => property.SetMethod) is { IsInitOnly: false };

    /// <summary>Whether the property has an init accessor.</summary>
    public bool HasInit => Accessor(Symbol, property => property.SetMethod) is { IsInitOnly: true };

    /// <summary>Whether the property is static.</summary>
    public bool IsStatic => Symbol.IsStatic;

    /// <summary>Whether the property is an indexer.</summary>
    public bool IsIndexer => Symbol.IsIndexer;

    /// <summary>Tests the substituted property type.</summary>
    public bool TypeIs(CodeType type) => type.Matches(Type);

    /// <summary>Tests the substituted property type.</summary>
    public bool TypeIs<T>() => TypeIs(CodeType.Of<T>());

    /// <summary>Whether the written property or indexer has this modifier token. A positional parameter's modifiers don't belong to the property, so they never match.</summary>
    public bool HasExplicitModifier(Modifier modifier) =>
        Origin != PropertyViewOrigin.PositionalParameter
        && DeclarationSyntax.HasModifier(Syntax, modifier);

    // An override can write one accessor and inherit the other.
    internal static IMethodSymbol? Accessor(
        IPropertySymbol property,
        Func<IPropertySymbol, IMethodSymbol?> select
    )
    {
        for (
            IPropertySymbol? current = property;
            current is not null;
            current = current.OverriddenProperty
        )
            if (select(current) is { } accessor)
                return accessor;
        return null;
    }

    /// <summary>The property identifier, positional parameter identifier, or indexer <c>this</c> keyword.</summary>
    public SourceLocation Location { get; }
}
