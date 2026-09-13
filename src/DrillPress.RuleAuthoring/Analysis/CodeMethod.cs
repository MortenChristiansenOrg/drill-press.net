using DrillPress.Flow;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress.Analysis;

/// <summary>A source method with lazy symbol binding and reusable body analysis.</summary>
public sealed class CodeMethod : ICodeElement
{
    private readonly Lazy<IMethodSymbol?> _symbol;
    private readonly Lazy<TestBody> _body;

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
        _body = new(() => new TestBody(this));
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
