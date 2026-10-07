using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>One written field variable. <c>private int a, b;</c> yields two fields that share their modifiers and type syntax.</summary>
public sealed class CodeField : ICodeDeclaration
{
    private readonly Lazy<IFieldSymbol?> _symbol;

    internal CodeField(AnalysisSource source, VariableDeclaratorSyntax syntax)
    {
        Source = source;
        Syntax = syntax;
        _symbol = new(() =>
            source.Model.GetDeclaredSymbol(syntax, source.Project.CancellationToken) as IFieldSymbol
        );
    }

    /// <summary>The original document and compilation membership.</summary>
    public AnalysisSource Source { get; }

    /// <summary>The written variable, including its initializer.</summary>
    public VariableDeclaratorSyntax Syntax { get; }

    /// <summary>The complete field declaration shared with other variables declared alongside it.</summary>
    public FieldDeclarationSyntax Declaration => (FieldDeclarationSyntax)Syntax.Parent!.Parent!;

    /// <summary>The declared field, or null when binding fails.</summary>
    public IFieldSymbol? Symbol => _symbol.Value;

    ISymbol? ICodeDeclaration.Symbol => Symbol;

    /// <summary>The declared identifier.</summary>
    public string Name => Syntax.Identifier.ValueText;

    /// <summary>Identifier words preserving casing, including acronym and digit boundaries.</summary>
    public IReadOnlyList<string> NameWords => CodeIdentifier.NamedWords(Name);

    /// <summary>The declared accessibility, including the implicit private default.</summary>
    public Accessibility Accessibility =>
        Symbol?.DeclaredAccessibility ?? Accessibility.NotApplicable;

    /// <summary>The field type, or null when binding fails.</summary>
    public ITypeSymbol? Type => Symbol?.Type;

    /// <summary>Matches the field type, including constructed generic arguments.</summary>
    public bool TypeIs(CodeType type) => Type is { } actual && type.Matches(actual);

    /// <summary>Matches the field type, including constructed generic arguments.</summary>
    public bool TypeIs<T>() => TypeIs(CodeType.Of<T>());

    /// <summary>Whether the field is a compile-time constant.</summary>
    public bool IsConst => HasExplicitModifier(Modifier.Const);

    /// <summary>Whether the field is readonly.</summary>
    public bool IsReadOnly => HasExplicitModifier(Modifier.ReadOnly);

    /// <summary>Whether the field is static, including constants.</summary>
    public bool IsStatic => IsConst || HasExplicitModifier(Modifier.Static);

    /// <summary>The written initializer value, if any.</summary>
    public CodeExpression? Initializer =>
        Syntax.Initializer?.Value is { } value ? new(Source, value) : null;

    /// <summary>Whether the written declaration contains this modifier token.</summary>
    public bool HasExplicitModifier(Modifier modifier) =>
        DeclarationSyntax.HasModifier(Declaration, modifier);

    /// <summary>The field name's physical span.</summary>
    public SourceLocation Location => Source.Locate(Syntax.Identifier.Span);
}
