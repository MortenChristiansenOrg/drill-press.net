using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>One written parameter of a method, constructor, operator, delegate, indexer, local function, primary constructor or lambda.</summary>
public sealed class CodeParameter : ICodeDeclaration
{
    private readonly Lazy<IParameterSymbol?> _symbol;

    internal CodeParameter(AnalysisSource source, ParameterSyntax syntax)
    {
        Source = source;
        Syntax = syntax;
        _symbol = new(() =>
            source.Model.GetDeclaredSymbol(syntax, source.Project.CancellationToken)
        );
    }

    /// <summary>The original document and compilation membership.</summary>
    public AnalysisSource Source { get; }

    /// <summary>The written parameter, including modifiers and default value.</summary>
    public ParameterSyntax Syntax { get; }

    /// <summary>The declared parameter, or null when binding fails.</summary>
    public IParameterSymbol? Symbol => _symbol.Value;

    ISymbol? ICodeDeclaration.Symbol => Symbol;

    /// <summary>The declared identifier.</summary>
    public string Name => Syntax.Identifier.ValueText;

    /// <summary>Identifier words preserving casing, including acronym and digit boundaries.</summary>
    public IReadOnlyList<string> NameWords => CodeIdentifier.NamedWords(Name);

    /// <summary>Always NotApplicable; parameters have no accessibility.</summary>
    public Accessibility Accessibility => Accessibility.NotApplicable;

    /// <summary>The parameter type, or null when binding fails or a lambda parameter has no inferable type.</summary>
    public ITypeSymbol? Type => Symbol?.Type;

    /// <summary>Matches the parameter type, including constructed generic arguments.</summary>
    public bool TypeIs(CodeType type) => Type is { } actual && type.Matches(actual);

    /// <summary>Matches the parameter type, including constructed generic arguments.</summary>
    public bool TypeIs<T>() => TypeIs(CodeType.Of<T>());

    /// <summary>The zero-based position in the written parameter list.</summary>
    public int Ordinal =>
        Syntax.Parent is BaseParameterListSyntax list ? list.Parameters.IndexOf(Syntax) : 0;

    /// <summary>The method, constructor, delegate invoke method, indexer or lambda declaring the parameter; null when binding fails.</summary>
    public ISymbol? ContainingSymbol => Symbol?.ContainingSymbol;

    /// <summary>Whether the parameter belongs to a lambda or anonymous method.</summary>
    public bool IsLambdaParameter =>
        Syntax.Parent is SimpleLambdaExpressionSyntax
        || Syntax.Parent?.Parent
            is ParenthesizedLambdaExpressionSyntax
                or AnonymousMethodExpressionSyntax;

    /// <summary>Whether a default value is written.</summary>
    public bool HasDefaultValue => Syntax.Default is not null;

    /// <summary>The written default value, if any.</summary>
    public CodeExpression? DefaultValue =>
        Syntax.Default?.Value is { } value ? new(Source, value) : null;

    /// <summary>Whether the written declaration contains this modifier token, such as ref, out, in, params or this.</summary>
    public bool HasExplicitModifier(Modifier modifier) =>
        DeclarationSyntax.HasModifier(Syntax, modifier);

    /// <summary>The parameter name's physical span.</summary>
    public SourceLocation Location => Source.Locate(Syntax.Identifier.Span);
}
