using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>A source method with lazy symbol binding and reusable body analysis.</summary>
public sealed class CodeMethod : ICodeDeclaration
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

    /// <summary>
    /// The source methods this method calls, in the order its calls are found, following statically bound targets. With
    /// <paramref name="sameType"/>, only methods of this method's type, including its other partial parts; with
    /// <paramref name="transitive"/>, also the methods those call, as in "the methods <c>ExecuteAsync</c> runs in its own type".
    /// </summary>
    /// <remarks>Calls in lambdas and local functions count unless <paramref name="nested"/> excludes them. Only ordinary
    /// methods are followed, not constructors, accessors, method groups, delegates, virtual dispatch or metadata. The method
    /// itself is included only when it is called, such as through recursion.</remarks>
    public IReadOnlyList<CodeMethod> Callees(
        bool sameType = false,
        bool transitive = false,
        NestedFunctions nested = NestedFunctions.Include
    )
    {
        var methods = Solution.Cached(
            _methodIndex,
            () => Solution.Shared("declarations", () => IndexMethods(Solution))
        );
        var owner = Symbol?.ContainingType?.OriginalDefinition;
        var found = new List<CodeMethod>();
        var seen = new HashSet<CodeMethod>();
        var pending = new Queue<CodeMethod>([this]);
        while (pending.TryDequeue(out var current))
        {
            foreach (var call in current.Body(nested)?.Calls() ?? [])
            {
                Solution.CancellationToken.ThrowIfCancellationRequested();
                var target = (call.Target.ReducedFrom ?? call.Target).OriginalDefinition;
                target = target.PartialImplementationPart ?? target;
                if (
                    sameType
                    && !SymbolEqualityComparer.Default.Equals(
                        target.ContainingType?.OriginalDefinition,
                        owner
                    )
                )
                    continue;
                foreach (var reference in target.DeclaringSyntaxReferences)
                {
                    if (
                        methods.TryGetValue(
                            (reference.SyntaxTree, reference.Span.Start),
                            out var callee
                        ) && seen.Add(callee)
                    )
                    {
                        found.Add(callee);
                        if (transitive)
                            pending.Enqueue(callee);
                    }
                }
            }
        }
        return found.AsReadOnly();
    }

    private static readonly object _methodIndex = new();

    private static Dictionary<(SyntaxTree Tree, int Start), CodeMethod> IndexMethods(
        AnalysisSolution solution
    ) => solution.Methods.ToDictionary(method => (method.Source.Tree, method.Syntax.SpanStart));

    /// <summary>The complete method declaration.</summary>
    public MethodDeclarationSyntax Syntax { get; }

    /// <summary>The declared symbol, or null for an unresolved declaration.</summary>
    public IMethodSymbol? Symbol => _symbol.Value;

    ISymbol? ICodeDeclaration.Symbol => Symbol;

    /// <summary>The declared identifier, available even when semantic binding fails.</summary>
    public string Name => Syntax.Identifier.ValueText;

    /// <summary>Identifier words preserving casing, including acronym and digit boundaries.</summary>
    public IReadOnlyList<string> NameWords => CodeIdentifier.NamedWords(Name);

    /// <summary>The declared accessibility, including the implicit private default; NotApplicable when unresolved.</summary>
    public Accessibility Accessibility =>
        Symbol?.DeclaredAccessibility ?? Accessibility.NotApplicable;

    /// <summary>Whether the written declaration contains this modifier token.</summary>
    public bool HasExplicitModifier(Modifier modifier) =>
        DeclarationSyntax.HasModifier(Syntax, modifier);

    /// <summary>Whether the method is static, from the written modifier when binding fails.</summary>
    public bool IsStatic => Symbol?.IsStatic ?? HasExplicitModifier(Modifier.Static);

    /// <summary>Whether the bound declaration is asynchronous; false for unresolved declarations.</summary>
    public bool IsAsync => Symbol?.IsAsync == true;

    /// <summary>Whether the bound method returns void; false for unresolved declarations.</summary>
    public bool ReturnsVoid => Symbol?.ReturnsVoid == true;

    /// <summary>Matches the bound return type, including constructed generic arguments.</summary>
    public bool ReturnTypeIs(CodeType type) =>
        Symbol?.ReturnType is { } actual && type.Matches(actual);

    /// <summary>Whether a block or expression body was written; abstract and extern declarations have no body.</summary>
    public bool HasBody => Syntax.Body is not null || Syntax.ExpressionBody is not null;

    /// <summary>Whether an existing block has no statements; a missing body is not empty.</summary>
    public bool HasEmptyBody => Syntax.Body is { Statements.Count: 0 };

    /// <summary>Whether the declaration has no body or an empty block; expression bodies always contain executable syntax.</summary>
    public bool HasNoStatements => !HasBody || HasEmptyBody;

    /// <summary>The written parameters in declaration order.</summary>
    public IReadOnlyList<CodeParameter> Parameters =>
        Array.AsReadOnly(
            Syntax
                .ParameterList.Parameters.Select(parameter => new CodeParameter(Source, parameter))
                .ToArray()
        );

    /// <summary>The executable body, excluding nested functions by default; null for abstract and extern declarations.</summary>
    public CodeBody? Body(NestedFunctions nested = NestedFunctions.Exclude) =>
        ((SyntaxNode?)Syntax.Body ?? Syntax.ExpressionBody?.Expression) is { } root
            ? new(Source, root, nested)
            : null;

    /// <summary>The containing source type when binding succeeds; malformed declarations retain their syntax candidate.</summary>
    public CodeTypeDefinition? ContainingType =>
        Symbol?.ContainingType is { } type
            ? Solution.Types.FirstOrDefault(candidate =>
                candidate.Source.Project == Source.Project
                && SymbolEqualityComparer.Default.Equals(candidate.Symbol, type.OriginalDefinition)
            )
            : null;

    /// <summary>The method identifier's physical span.</summary>
    public SourceLocation Location => Source.Locate(Syntax.Identifier.Span);
}
