using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>A named declaration: a type, method, field, property, parameter or other declared symbol. Name, attribute, modifier and accessibility predicates work the same for every kind.</summary>
public interface ICodeDeclaration : ICodeElement
{
    /// <summary>The declared identifier, available even when binding fails.</summary>
    string Name { get; }

    /// <summary>Identifier words preserving casing, split at underscores, case transitions, acronyms and digits.</summary>
    IReadOnlyList<string> NameWords { get; }

    /// <summary>The declared compiler symbol, or null when binding fails.</summary>
    ISymbol? Symbol { get; }

    /// <summary>The compiler's declared accessibility, including implicit defaults; NotApplicable for parameters and unresolved declarations.</summary>
    Accessibility Accessibility { get; }

    /// <summary>Whether a written declaration contains this modifier token, independently of implicit defaults. Partial types combine their parts.</summary>
    bool HasExplicitModifier(Modifier modifier);
}
