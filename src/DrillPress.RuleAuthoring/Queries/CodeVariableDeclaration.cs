using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>A written local, foreach or declaration-expression type, preserving the containing declaration group.</summary>
public sealed class CodeVariableDeclaration : ICodeElement
{
    private readonly Lazy<bool> _canUseVar;

    internal CodeVariableDeclaration(AnalysisSource source, SyntaxNode syntax, TypeSyntax type)
    {
        Source = source;
        Syntax = syntax;
        TypeSyntax = type;
        _canUseVar = new(() => VarRewrite.CanUseVar(this));
    }

    /// <summary>The original source membership.</summary>
    public AnalysisSource Source { get; }

    /// <summary>The complete declaration syntax; multi-declarator locals are never split implicitly.</summary>
    public SyntaxNode Syntax { get; }

    /// <summary>The original written type syntax.</summary>
    public TypeSyntax TypeSyntax { get; }

    /// <summary>Whether replacing this explicit type with var preserves the inferred local type, nullability, tuple names and enclosing binding. Unsupported or unresolved declarations return false.</summary>
    /// <remarks>Multi-declarator locals and value-producing target-typed expressions are excluded. The result is cached for this declaration.</remarks>
    public bool CanUseVar => _canUseVar.Value;

    /// <summary>A reportable type-name element belonging to the original context.</summary>
    public CodeNode<TypeSyntax> TypeName => new(Source, TypeSyntax);

    /// <summary>The declaration span.</summary>
    public SourceLocation Location => Source.Locate(Syntax.Span);

    /// <summary>Whether the type is explicit; an actual type or alias named var remains explicit. Unresolved types are retained as explicit syntax.</summary>
    public bool HasExplicitType =>
        !TypeSyntax.IsVar
        || Source.Model.GetAliasInfo(TypeSyntax, Source.Project.CancellationToken) is not null
        || Source.Model.GetSymbolInfo(TypeSyntax, Source.Project.CancellationToken).Symbol
            is INamedTypeSymbol { Name: "var" };

    /// <summary>Every written local variable in this group; foreach/out bindings are represented by their own syntax rather than fabricated declarators.</summary>
    public IReadOnlyList<CodeNode<VariableDeclaratorSyntax>> Variables =>
        Syntax is VariableDeclarationSyntax declaration
            ? Array.AsReadOnly(
                declaration
                    .Variables.Select(variable => new CodeNode<VariableDeclaratorSyntax>(
                        Source,
                        variable
                    ))
                    .ToArray()
            )
            : [];

    /// <summary>Whether this local declaration has at least one variable and every variable has an initializer.</summary>
    public bool HasInitializer =>
        Syntax is VariableDeclarationSyntax { Variables.Count: > 0 } declaration
        && declaration.Variables.All(variable => variable.Initializer is not null);

    /// <summary>Whether this is a local declaration statement in a block or switch section that declares one initialized local, neither <c>const</c>, <c>ref</c> nor <c>using</c>, that is referenced exactly once in its member and never written or passed by reference: the shape <see cref="LocalInlining"/> inlines. Unresolved members return false.</summary>
    public bool HasSingleRead =>
        Syntax is VariableDeclarationSyntax declaration
        && LocalReferences.SingleRead(Source, declaration) is not null;
}
