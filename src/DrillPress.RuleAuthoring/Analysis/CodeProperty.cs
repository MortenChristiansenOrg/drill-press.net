using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>One written property declaration; indexers are excluded.</summary>
public sealed class CodeProperty : ICodeDeclaration
{
    private readonly Lazy<IPropertySymbol?> _symbol;

    internal CodeProperty(AnalysisSource source, PropertyDeclarationSyntax syntax)
    {
        Source = source;
        Syntax = syntax;
        _symbol = new(() =>
            source.Model.GetDeclaredSymbol(syntax, source.Project.CancellationToken)
        );
    }

    /// <summary>The original document and compilation membership.</summary>
    public AnalysisSource Source { get; }

    /// <summary>The complete written property.</summary>
    public PropertyDeclarationSyntax Syntax { get; }

    /// <summary>The declared property, or null when binding fails.</summary>
    public IPropertySymbol? Symbol => _symbol.Value;

    ISymbol? ICodeDeclaration.Symbol => Symbol;

    /// <summary>The declared identifier.</summary>
    public string Name => Syntax.Identifier.ValueText;

    /// <summary>Identifier words preserving casing, including acronym and digit boundaries.</summary>
    public IReadOnlyList<string> NameWords => CodeIdentifier.NamedWords(Name);

    /// <summary>The declared accessibility, including the implicit private default.</summary>
    public Accessibility Accessibility =>
        Symbol?.DeclaredAccessibility ?? Accessibility.NotApplicable;

    /// <summary>The property type, or null when binding fails.</summary>
    public ITypeSymbol? Type => Symbol?.Type;

    /// <summary>Matches the property type, including constructed generic arguments.</summary>
    public bool TypeIs(CodeType type) => Type is { } actual && type.Matches(actual);

    /// <summary>Matches the property type, including constructed generic arguments.</summary>
    public bool TypeIs<T>() => TypeIs(CodeType.Of<T>());

    /// <summary>Whether the property is static.</summary>
    public bool IsStatic => HasExplicitModifier(Modifier.Static);

    /// <summary>Whether a getter is declared, including an expression-bodied property.</summary>
    public bool HasGetter =>
        Syntax.ExpressionBody is not null
        || Accessor(SyntaxKind.GetAccessorDeclaration) is not null;

    /// <summary>Whether a set accessor is declared.</summary>
    public bool HasSetter => Accessor(SyntaxKind.SetAccessorDeclaration) is not null;

    /// <summary>Whether an init accessor is declared.</summary>
    public bool HasInit => Accessor(SyntaxKind.InitAccessorDeclaration) is not null;

    /// <summary>Whether every accessor is written without a body, so the compiler supplies the storage.</summary>
    public bool IsAutoProperty =>
        Syntax.ExpressionBody is null
        && Syntax.AccessorList is { Accessors.Count: > 0 } accessors
        && accessors.Accessors.All(accessor =>
            accessor.Body is null && accessor.ExpressionBody is null
        );

    /// <summary>The written initializer value, if any.</summary>
    public CodeExpression? Initializer =>
        Syntax.Initializer?.Value is { } value ? new(Source, value) : null;

    /// <summary>Whether the written declaration contains this modifier token.</summary>
    public bool HasExplicitModifier(Modifier modifier) =>
        DeclarationSyntax.HasModifier(Syntax, modifier);

    /// <summary>The property name's physical span.</summary>
    public SourceLocation Location => Source.Locate(Syntax.Identifier.Span);

    private AccessorDeclarationSyntax? Accessor(SyntaxKind kind) =>
        Syntax.AccessorList?.Accessors.FirstOrDefault(accessor => accessor.IsKind(kind));
}
