using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

/// <summary>
/// A source function call paths can reach: a method, local function, constructor, accessor or operator. Lambdas and
/// anonymous methods belong to the function that creates them. One instance represents each declaration per analysis.
/// </summary>
public sealed class CodeFunction : ICodeElement
{
    private readonly CallGraph _graph;
    private readonly Lazy<IMethodSymbol?> _symbol;
    private IReadOnlyList<CallSite>? _sites;

    internal CodeFunction(
        CallGraph graph,
        AnalysisSource source,
        SyntaxNode syntax,
        FunctionKind kind,
        bool isImplicit
    )
    {
        _graph = graph;
        Solution = graph.Solution;
        Source = source;
        Syntax = syntax;
        Kind = kind;
        IsImplicit = isImplicit;
        _symbol = new(Bind);
    }

    /// <summary>The analysis owning this function and its cached call paths.</summary>
    public AnalysisSolution Solution { get; }

    /// <summary>The document and compilation declaring this function; generated documents are included.</summary>
    public AnalysisSource Source { get; }

    /// <summary>
    /// The declaration: a method, local function, constructor, accessor or operator declaration, an expression-bodied
    /// property's arrow clause, or the type declaration of a primary or implicit constructor.
    /// </summary>
    public SyntaxNode Syntax { get; }

    /// <summary>The kind of declaration; <see cref="IMethodSymbol.MethodKind"/> distinguishes getters, setters and event accessors.</summary>
    public FunctionKind Kind { get; }

    // The parameterless constructor the compiler declares for a type without one.
    internal bool IsImplicit { get; }

    /// <summary>The declared symbol, or null for an unresolved declaration.</summary>
    public IMethodSymbol? Symbol => _symbol.Value;

    /// <summary>The compiler member name, such as <c>Run</c>, <c>.ctor</c> or <c>get_Value</c>; a written method or local function name when binding fails.</summary>
    public string Name =>
        Symbol?.Name
        ?? Syntax switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            LocalFunctionStatementSyntax local => local.Identifier.ValueText,
            _ => "",
        };

    /// <summary>The ordinary method this function is, or null for other kinds and generated methods.</summary>
    public CodeMethod? Method => Kind == FunctionKind.Method ? _graph.MethodAt(this) : null;

    /// <summary>The declared name: the identifier, accessor keyword, property name, operator token or type name.</summary>
    public SourceLocation Location => Source.Locate(NameSpan);

    private TextSpan NameSpan =>
        Syntax switch
        {
            MethodDeclarationSyntax method => method.Identifier.Span,
            LocalFunctionStatementSyntax local => local.Identifier.Span,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.Span,
            AccessorDeclarationSyntax accessor => accessor.Keyword.Span,
            ArrowExpressionClauseSyntax { Parent: PropertyDeclarationSyntax property } => property
                .Identifier
                .Span,
            ArrowExpressionClauseSyntax { Parent: IndexerDeclarationSyntax indexer } => indexer
                .ThisKeyword
                .Span,
            OperatorDeclarationSyntax @operator => @operator.OperatorToken.Span,
            ConversionOperatorDeclarationSyntax conversion => conversion.Type.Span,
            BaseTypeDeclarationSyntax type => type.Identifier.Span,
            _ => Syntax.Span,
        };

    /// <summary>The written block or expression body; null for bodyless declarations and primary or implicit constructors.</summary>
    /// <remarks>A constructor's initializer and the member initializers it runs are outside its body; call-path queries still follow them.</remarks>
    public CodeBody? Body(NestedFunctions nested = NestedFunctions.Exclude) =>
        Syntax switch
        {
            BaseMethodDeclarationSyntax method => (SyntaxNode?)method.Body
                ?? method.ExpressionBody?.Expression,
            AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body
                ?? accessor.ExpressionBody?.Expression,
            LocalFunctionStatementSyntax local => (SyntaxNode?)local.Body
                ?? local.ExpressionBody?.Expression,
            ArrowExpressionClauseSyntax arrow => arrow.Expression,
            _ => null,
        }
            is { } root
            ? new(Source, root, nested)
            : null;

    /// <summary>
    /// Whether a call path from this function runs the target: a method, constructor, or property or event accessor
    /// matching <paramref name="target"/>. A property target matches its accessors.
    /// </summary>
    /// <param name="target">The configured API.</param>
    /// <param name="paths">The edges to follow; <see cref="CallPaths.Bound"/> by default.</param>
    /// <remarks>False doesn't prove that runtime execution can't reach the target. Answers are cached per analysis, target instance and configuration.</remarks>
    public bool Reaches(CodeMember target, CallPaths? paths = null) =>
        _graph.Reaches(this, target, paths ?? CallPaths.Bound);

    /// <summary>The shortest call path from this function to a site that runs the target, or null when <see cref="Reaches"/> is false.</summary>
    /// <remarks>Use the steps' <see cref="CallStep.Kind"/> and <see cref="CallStep.ViaDelegate"/> to tell bound calls from dispatch, configured edges and delegates that may not run.</remarks>
    public CallPath? PathTo(CodeMember target, CallPaths? paths = null) =>
        _graph.PathTo(this, target, paths ?? CallPaths.Bound);

    /// <summary>
    /// The source functions this function calls, in the order its sites are found. With <paramref name="sameType"/>,
    /// only functions of this function's type, including its other partial parts and local functions; with
    /// <paramref name="transitive"/>, also the functions those call, breadth first.
    /// </summary>
    /// <remarks>Each function appears once; this function appears only when it is called, such as through recursion. Members declared in metadata have no function.</remarks>
    public IReadOnlyList<CodeFunction> Callees(
        bool sameType = false,
        bool transitive = false,
        CallPaths? paths = null
    ) => _graph.Callees(this, sameType, transitive, paths ?? CallPaths.Bound);

    /// <summary>The steps this function's own sites take, in source order, including calls to metadata members.</summary>
    public IReadOnlyList<CallStep> Steps(CallPaths? paths = null) =>
        _graph.Steps(this, paths ?? CallPaths.Bound);

    internal IReadOnlyList<CallSite> Sites =>
        LazyInitializer.EnsureInitialized(ref _sites, () => _graph.Discover(this));

    private IMethodSymbol? Bind()
    {
        var token = Source.Project.CancellationToken;
        var model = Source.Model;
        return Syntax switch
        {
            ArrowExpressionClauseSyntax { Parent: { } owner } => (
                model.GetDeclaredSymbol(owner, token) as IPropertySymbol
            )?.GetMethod,
            BaseTypeDeclarationSyntax type => model.GetDeclaredSymbol(type, token)
                is INamedTypeSymbol declared
                ? declared.InstanceConstructors.FirstOrDefault(constructor =>
                    IsImplicit
                        ? constructor.IsImplicitlyDeclared && constructor.Parameters.IsEmpty
                        : constructor.DeclaringSyntaxReferences.Any(reference =>
                            reference.SyntaxTree == Source.Tree && reference.Span == type.Span
                        )
                )
                : null,
            _ => model.GetDeclaredSymbol(Syntax, token) as IMethodSymbol,
        };
    }

    /// <inheritdoc/>
    public override string ToString() => Name;
}
