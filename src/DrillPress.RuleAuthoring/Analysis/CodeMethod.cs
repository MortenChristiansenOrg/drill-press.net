using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress.Analysis;

/// <summary>A source method with lazy symbol binding and reusable body analysis.</summary>
public sealed class CodeMethod : ICodeElement
{
    private readonly Lazy<IMethodSymbol?> _symbol;
    private readonly Lazy<TestBody> _body;

    internal CodeMethod(AnalysisSource source, MethodDeclarationSyntax syntax)
    {
        Source = source;
        Syntax = syntax;
        _symbol = new(() =>
            source.Model.GetDeclaredSymbol(syntax, source.Project.CancellationToken)
        );
        _body = new(() => new TestBody(this));
    }

    /// <summary>The document and compilation used to bind this declaration.</summary>
    public AnalysisSource Source { get; }

    /// <summary>The complete method declaration.</summary>
    public MethodDeclarationSyntax Syntax { get; }

    /// <summary>The declared symbol, or null for an unresolved declaration.</summary>
    public IMethodSymbol? Symbol => _symbol.Value;

    /// <summary>The declared identifier, available even when semantic binding fails.</summary>
    public string Name => Syntax.Identifier.ValueText;

    /// <summary>Whether the bound declaration is asynchronous; false for unresolved declarations.</summary>
    public bool IsAsync => Symbol?.IsAsync == true;

    /// <summary>Tests semantic attributes; unresolved declarations do not match.</summary>
    public bool HasAttribute(CodeType attribute) =>
        Symbol is { } symbol && Symbols.HasAttribute(symbol, attribute);

    /// <summary>The method identifier's physical span.</summary>
    public SourceLocation Location => Source.Locate(Syntax.Identifier.Span);

    /// <summary>Shared physical blank-line and assertion analysis, excluding nested bodies.</summary>
    public TestBody Body => _body.Value;
}
