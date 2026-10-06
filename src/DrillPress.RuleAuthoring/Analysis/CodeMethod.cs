using DrillPress;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>A source method with lazy symbol binding and reusable body analysis.</summary>
public sealed class CodeMethod : ICodeElement
{
    private readonly Lazy<IMethodSymbol?> _symbol;

    internal CodeMethod(
        AnalysisSolution solution,
        AnalysisSource source,
        MethodDeclarationSyntax syntax
    )
    {
        Solution = solution;
        Source = source;
        Syntax = syntax;
        _symbol = new(() =>
            source.Model.GetDeclaredSymbol(syntax, source.Project.CancellationToken)
        );
    }

    /// <summary>The document and compilation used to bind this declaration.</summary>
    public AnalysisSource Source { get; }

    /// <summary>The analysis owning this method and its cached relationships.</summary>
    public AnalysisSolution Solution { get; }

    /// <summary>Cached compiler control flow, reads, writes, captures and nullable state for this method.</summary>
    public MethodFlow Flow => MethodFlow.For(Solution, this);

    /// <summary>Whether a statically bound source call path reaches the target. Unresolved methods do not match.</summary>
    /// <remarks>Does not infer dynamic dispatch, delegate invocation or reflection. Nested functions are followed only through direct calls.</remarks>
    public bool Reaches(CodeMember target) =>
        Symbol is { } symbol && Solution.Relationships.Reaches(symbol, target);

    /// <summary>The complete method declaration.</summary>
    public MethodDeclarationSyntax Syntax { get; }

    /// <summary>The declared symbol, or null for an unresolved declaration.</summary>
    public IMethodSymbol? Symbol => _symbol.Value;

    /// <summary>The declared identifier, available even when semantic binding fails.</summary>
    public string Name => Syntax.Identifier.ValueText;

    /// <summary>Identifier words preserving casing, including acronym and digit boundaries.</summary>
    public IReadOnlyList<string> NameWords => CodeIdentifier.NamedWords(Name);

    /// <summary>Whether a block or expression body was written; abstract and extern declarations have no body.</summary>
    public bool HasBody => Syntax.Body is not null || Syntax.ExpressionBody is not null;

    /// <summary>Whether an existing block has no statements; a missing body is not empty.</summary>
    public bool HasEmptyBody => Syntax.Body is { Statements.Count: 0 };

    /// <summary>Whether the declaration has no body or an empty block; expression bodies always contain executable syntax.</summary>
    public bool HasNoStatements => !HasBody || HasEmptyBody;

    /// <summary>Whether the bound declaration is asynchronous; false for unresolved declarations.</summary>
    public bool IsAsync => Symbol?.IsAsync == true;

    /// <summary>Tests semantic attributes; unresolved declarations do not match.</summary>
    public bool HasAttribute(CodeType attribute) =>
        Symbol is { } symbol && Symbols.HasAttribute(symbol, attribute);

    /// <summary>Matches an applied attribute and its recorded values; unresolved methods do not match.</summary>
    public bool HasAttribute(CodeType attribute, Func<CodeAttribute, bool> where) =>
        Symbol?.HasAttribute(attribute, where) == true;

    /// <summary>The containing source type when binding succeeds; malformed declarations retain their syntax candidate.</summary>
    public CodeDeclaration? ContainingType =>
        Symbol?.ContainingType is { } type
            ? Solution.Types.FirstOrDefault(candidate =>
                candidate.Source.Project == Source.Project
                && SymbolEqualityComparer.Default.Equals(candidate.Symbol, type.OriginalDefinition)
            )
            : null;

    /// <summary>The method identifier's physical span.</summary>
    public SourceLocation Location => Source.Locate(Syntax.Identifier.Span);
}
