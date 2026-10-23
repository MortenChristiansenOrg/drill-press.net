using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>One written enum member, such as <c>Pending</c> or <c>Started = 5</c>.</summary>
public sealed class CodeEnumMember : ICodeDeclaration
{
    private readonly Lazy<IFieldSymbol?> _symbol;

    internal CodeEnumMember(AnalysisSource source, EnumMemberDeclarationSyntax syntax)
    {
        Source = source;
        Syntax = syntax;
        _symbol = new(() =>
            source.Model.GetDeclaredSymbol(syntax, source.Project.CancellationToken) as IFieldSymbol
        );
    }

    /// <summary>The original document and compilation membership.</summary>
    public AnalysisSource Source { get; }

    /// <summary>The written member, including its attributes and value.</summary>
    public EnumMemberDeclarationSyntax Syntax { get; }

    /// <summary>The declared constant field, or null when binding fails.</summary>
    public IFieldSymbol? Symbol => _symbol.Value;

    ISymbol? ICodeDeclaration.Symbol => Symbol;

    /// <summary>The declared identifier.</summary>
    public string Name => Syntax.Identifier.ValueText;

    /// <summary>Identifier words preserving casing, including acronym and digit boundaries.</summary>
    public IReadOnlyList<string> NameWords => CodeIdentifier.NamedWords(Name);

    /// <summary>The enum's accessibility, which its members share; NotApplicable when binding fails.</summary>
    public Accessibility Accessibility =>
        Symbol?.DeclaredAccessibility ?? Accessibility.NotApplicable;

    /// <summary>The enum type, or null when binding fails.</summary>
    public INamedTypeSymbol? ContainingType => Symbol?.ContainingType;

    /// <summary>The member's constant value, written or implied by its position; null when binding fails.</summary>
    public object? Value => Symbol?.ConstantValue;

    /// <summary>Whether the value is written, as in <c>Started = 5</c>.</summary>
    public bool HasExplicitValue => Syntax.EqualsValue is not null;

    /// <summary>Enum members have no modifiers.</summary>
    public bool HasExplicitModifier(Modifier modifier) =>
        DeclarationSyntax.HasModifier(Syntax, modifier);

    /// <summary>The member name's physical span.</summary>
    public SourceLocation Location => Source.Locate(Syntax.Identifier.Span);
}
