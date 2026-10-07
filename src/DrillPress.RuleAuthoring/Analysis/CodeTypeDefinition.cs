using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>One distinct named type definition per compilation, reported at a deterministic ordinary-source declaration. Use <see cref="CodeTypeDeclaration"/> for individual partial parts.</summary>
public sealed class CodeTypeDefinition : ICodeDeclaration
{
    internal CodeTypeDefinition(
        AnalysisSolution solution,
        AnalysisSource source,
        MemberDeclarationSyntax syntax,
        INamedTypeSymbol symbol
    )
    {
        Solution = solution;
        Source = source;
        Syntax = syntax;
        Symbol = symbol;
        Location = source.Locate(
            syntax switch
            {
                BaseTypeDeclarationSyntax type => type.Identifier.Span,
                DelegateDeclarationSyntax declaration => declaration.Identifier.Span,
                _ => throw new ArgumentException(
                    "A type candidate requires a named type declaration.",
                    nameof(syntax)
                ),
            }
        );
    }

    /// <summary>The shared graph used by cross-project conditions.</summary>
    public AnalysisSolution Solution { get; }

    /// <summary>The selected ordinary source declaration's context.</summary>
    public AnalysisSource Source { get; }

    /// <summary>The selected declaration; partial declarations share one candidate.</summary>
    public MemberDeclarationSyntax Syntax { get; }

    /// <summary>The bound original type definition.</summary>
    public INamedTypeSymbol Symbol { get; }

    ISymbol? ICodeDeclaration.Symbol => Symbol;

    /// <summary>The unqualified declared type name.</summary>
    public string Name => Symbol.Name;

    /// <summary>Identifier words preserving casing, including acronym and digit boundaries.</summary>
    public IReadOnlyList<string> NameWords => CodeIdentifier.NamedWords(Name);

    /// <summary>The declared namespace, or an empty string for the global namespace.</summary>
    public string Namespace =>
        Symbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : Symbol.ContainingNamespace.ToDisplayString();

    /// <summary>The declared accessibility, including the implicit internal or private default.</summary>
    public Accessibility Accessibility => Symbol.DeclaredAccessibility;

    /// <summary>Whether any partial part of this type writes the modifier token.</summary>
    public bool HasExplicitModifier(Modifier modifier) =>
        Symbol.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(Source.Project.CancellationToken) is { } part
            && DeclarationSyntax.HasModifier(part, modifier)
        );

    /// <summary>Whether this is a class, including records and abstract or static classes.</summary>
    public bool IsClass => Symbol.TypeKind == TypeKind.Class;

    /// <summary>Whether this is an interface.</summary>
    public bool IsInterface => Symbol.TypeKind == TypeKind.Interface;

    /// <summary>Whether this is a struct, including record structs.</summary>
    public bool IsStruct => Symbol.TypeKind == TypeKind.Struct;

    /// <summary>Whether this is an enum.</summary>
    public bool IsEnum => Symbol.TypeKind == TypeKind.Enum;

    /// <summary>Whether this is a record class or record struct.</summary>
    public bool IsRecord => Symbol.IsRecord;

    /// <summary>Whether the type is abstract, including interfaces.</summary>
    public bool IsAbstract => Symbol.IsAbstract;

    /// <summary>Whether the type is static.</summary>
    public bool IsStatic => Symbol.IsStatic;

    /// <summary>Whether the type is nested in another type.</summary>
    public bool IsNested => Symbol.ContainingType is not null;

    /// <summary>Tests all inherited and constructed interfaces by semantic identity.</summary>
    public bool Implements(CodeType contract) => Symbols.Implements(Symbol, contract);

    /// <summary>Tests the strict base-class chain, excluding this definition.</summary>
    public bool DerivesFrom(CodeType type) => Symbol.DerivesFrom(type);

    /// <summary>Tests this definition and its base-class chain.</summary>
    public bool IsOrDerivesFrom(CodeType type) => Symbol.IsOrDerivesFrom(type);

    /// <summary>The selected type identifier's physical span.</summary>
    public SourceLocation Location { get; }
}
