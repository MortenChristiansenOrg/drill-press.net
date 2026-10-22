using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>An immutable rename of one symbol and all of its references.</summary>
/// <remarks>
/// Gates: a supported symbol (not virtual, abstract, overriding, implementing an interface, extern, partial or an entry point) whose declarations and references are all in editable ordinary source; no identifier spelled like the new name and no string spelled like the old name in the searched scope; and, in every affected compilation, every renamed token bound to the renamed symbol, every other expression in the scope binding as before, and unchanged compiler-supplied arguments.
/// The scope is the containing member for locals and local-function parameters, the containing type for private members and parameters of private methods, and every loaded project otherwise.
/// </remarks>
public sealed class SymbolRename
{
    private readonly AnalysisSource _source;
    private readonly ISymbol? _symbol;
    private readonly string _name;
    private readonly string _text;

    internal SymbolRename(AnalysisSource source, ISymbol? symbol, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        var bare = newName.StartsWith('@') ? newName[1..] : newName;
        if (!SyntaxFacts.IsValidIdentifier(bare))
            throw new ArgumentException("Use a valid C# identifier.", nameof(newName));
        _source = source;
        _symbol = symbol;
        _name = bare;
        _text = SyntaxFacts.GetKeywordKind(bare) != SyntaxKind.None ? "@" + bare : bare;
    }

    /// <summary>Proposes a rename whose names cannot be observed outside the containing member or type: locals, local functions, parameters of private methods and local functions, and private fields, properties and methods. The library owns this proof.</summary>
    public FixProposal? Propose() => Plan(wider: false, _ => true);

    /// <summary>Proposes any supported rename when your proof holds in every affected compilation. For symbols visible outside their type, the proof states that reflection, serialization, model binding and unloaded assemblies do not depend on the old name; the library still requires every reference to be in loaded, editable source.</summary>
    public FixProposal? SafeWhen(Func<RenameChange, bool> proof) => Plan(wider: true, proof);

    private FixProposal Plan(bool wider, Func<RenameChange, bool> proof)
    {
        if (_symbol is null)
            return FixProposal.Withheld("the declaration has no symbol");
        if (_symbol.Name == _name)
            return FixProposal.Withheld("the symbol already has that name");
        if (Unsupported(_symbol) is { } unsupported)
            return FixProposal.Withheld(unsupported);
        var local = IsLocal(_symbol);
        if (!wider && !local)
            return FixProposal.Withheld(
                "the name is visible outside its type, where reflection, serialization or unloaded code can depend on it; use SafeWhen"
            );
        var sources = Scope(_source, _symbol, local) is { } scope
            ? scope.Select(node => (Source: _source, Node: node))
            : (_source.Project.Solution?.Projects ?? [_source.Project])
                .SelectMany(project => project.Sources)
                .Select(source =>
                    (Source: source, Node: source.Tree.GetRoot(source.Project.CancellationToken))
                );
        var searched = sources.ToArray();
        var tokens = new List<(AnalysisSource Source, SyntaxToken Token)>();
        foreach (var (source, node) in searched)
        {
            if (Obstacle(source, node) is { } obstacle)
                return FixProposal.Withheld(obstacle);
            tokens.AddRange(References(source, node).Select(token => (source, token)));
        }
        foreach (var reference in _symbol.DeclaringSyntaxReferences)
        {
            var declaration = reference.GetSyntax(_source.Project.CancellationToken);
            var source = searched
                .Select(item => item.Source)
                .FirstOrDefault(candidate => candidate.Tree == reference.SyntaxTree);
            if (source is null || Identifier(declaration) is not { RawKind: not 0 } identifier)
                return FixProposal.Withheld(
                    "a declaration of the symbol is outside the searched source"
                );
            tokens.Add((source, identifier));
        }
        if (
            tokens.Any(item => !item.Source.Document.IsEditable || item.Source.Document.IsGenerated)
        )
            return FixProposal.Withheld(
                "a declaration or reference is in generated or non-editable source"
            );
        if (OutsideDeclaringProjects(tokens.Select(item => item.Source)) is { } outside)
            return FixProposal.Withheld(outside);
        var edits = tokens
            .Select(item => SourceChanges.Replace(item.Source, item.Token.Span, _text))
            .DistinctBy(edit => (edit.FileIdentity, edit.Start))
            .OrderBy(edit => edit.FileIdentity, StringComparer.Ordinal)
            .ThenBy(edit => edit.Start)
            .ToArray();
        var declarations = _symbol
            .DeclaringSyntaxReferences.Select(reference => (reference.SyntaxTree, reference.Span))
            .ToArray();
        return SourceChanges.ProposeExplained(
            edits,
            context => Validate(context, edits, declarations, local, proof)
        );
    }

    private static string? Unsupported(ISymbol symbol) =>
        symbol switch
        {
            ILocalSymbol
            or IFieldSymbol
            {
                IsImplicitlyDeclared: false,
                ContainingType.TypeKind: not TypeKind.Enum
            } => null,
            IParameterSymbol { ContainingSymbol: IMethodSymbol method }
                when method.MethodKind == MethodKind.AnonymousFunction =>
                "lambda parameter names can be bound by name, as in minimal API handlers",
            IParameterSymbol
            {
                ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
            } => constructor.ContainingType.IsRecord
                ? "record parameters also name generated properties"
                : null,
            IParameterSymbol
            {
                ContainingSymbol: IMethodSymbol
                {
                    MethodKind: MethodKind.Ordinary or MethodKind.LocalFunction
                } method
            } => Unsupported(method) is { } reason
                ? "the parameter's method cannot be renamed safely: " + reason
                : null,
            IParameterSymbol =>
                "the library renames only parameters of methods, local functions and constructors",
            IPropertySymbol { IsIndexer: false } property => Member(property),
            IMethodSymbol { MethodKind: MethodKind.LocalFunction } => null,
            IMethodSymbol { MethodKind: MethodKind.Ordinary } method => method.PartialDefinitionPart
                is not null
            || method.PartialImplementationPart is not null
                ? "the method is partial"
            : method.Name == "Main" && method.IsStatic ? "the method may be the entry point"
            : Member(method),
            _ =>
                "the library renames locals, parameters, fields, properties, methods and local functions",
        };

    private static string? Member(ISymbol member) =>
        member.IsVirtual || member.IsAbstract || member.IsOverride || member.IsExtern
            ? "the member is virtual, abstract, overriding or extern"
        : member.ContainingType is { } type
        && type.AllInterfaces.SelectMany(contract => contract.GetMembers())
            .Any(candidate =>
                SymbolEqualityComparer.Default.Equals(
                    type.FindImplementationForInterfaceMember(candidate),
                    member
                )
            )
            ? "the member implements an interface"
        : member
            is IPropertySymbol { ExplicitInterfaceImplementations.Length: > 0 }
                or IMethodSymbol { ExplicitInterfaceImplementations.Length: > 0 }
            ? "the member implements an interface"
        : null;

    // Names that only code in the containing member or type can observe.
    private static bool IsLocal(ISymbol symbol) =>
        symbol switch
        {
            ILocalSymbol => true,
            IMethodSymbol { MethodKind: MethodKind.LocalFunction } => true,
            IParameterSymbol { ContainingSymbol: IMethodSymbol method } => method.MethodKind
                == MethodKind.LocalFunction
                || method.DeclaredAccessibility == Accessibility.Private
                    && method.MethodKind == MethodKind.Ordinary,
            _ => symbol.DeclaredAccessibility == Accessibility.Private,
        };

    // The member for locals, the outermost containing type's parts for private members, or null for the whole solution.
    private static IReadOnlyList<SyntaxNode>? Scope(
        AnalysisSource source,
        ISymbol symbol,
        bool local
    )
    {
        if (!local)
            return null;
        var declaration = symbol
            .DeclaringSyntaxReferences.FirstOrDefault()
            ?.GetSyntax(source.Project.CancellationToken);
        if (declaration is null)
            return null;
        if (
            symbol is ILocalSymbol or IMethodSymbol { MethodKind: MethodKind.LocalFunction }
            || symbol
                is IParameterSymbol
                {
                    ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.LocalFunction }
                }
        )
            return
                declaration
                    .Ancestors()
                    .FirstOrDefault(node =>
                        node
                            is MemberDeclarationSyntax
                                and not BaseTypeDeclarationSyntax
                                and not BaseNamespaceDeclarationSyntax
                    )
                    is { } member
                ? member is GlobalStatementSyntax
                    ? [member.Parent!]
                    : [member]
                : null;
        var type = symbol.ContainingType;
        while (type?.ContainingType is { } outer)
            type = outer;
        // Parts of a partial type in several files fall back to searching every loaded source.
        return
            type is not null
            && type.DeclaringSyntaxReferences.All(reference => reference.SyntaxTree == source.Tree)
            ? type
                .DeclaringSyntaxReferences.Select(reference =>
                    reference.GetSyntax(source.Project.CancellationToken)
                )
                .ToArray()
            : null;
    }

    // An identifier spelled like the new name could be captured or shadowed, and a string spelled like the old
    // name may refer to it by reflection or in an attribute such as NotNullIfNotNull.
    private string? Obstacle(AnalysisSource source, SyntaxNode node)
    {
        foreach (var token in node.DescendantTokens(descendIntoTrivia: true))
        {
            if (token.IsKind(SyntaxKind.IdentifierToken) && token.ValueText == _name)
                return $"`{_name}` is already used at {FixReasons.Position(source, token.SpanStart)}";
            if (token.IsKind(SyntaxKind.StringLiteralToken) && token.ValueText == _symbol!.Name)
                return $"the string \"{_symbol.Name}\" at {FixReasons.Position(source, token.SpanStart)} may refer to the symbol by name";
        }
        return null;
    }

    private IEnumerable<SyntaxToken> References(AnalysisSource source, SyntaxNode node)
    {
        var identity = RewriteSymbols.Identity(_symbol!.OriginalDefinition);
        foreach (
            var name in node.DescendantNodes(descendIntoTrivia: true).OfType<IdentifierNameSyntax>()
        )
        {
            if (name.Identifier.ValueText != _symbol.Name)
                continue;
            var bound = Resolve(source.Model, name);
            if (
                bound is not null
                && RewriteSymbols.Identity(bound.OriginalDefinition) == identity
                && SameDeclaration(bound)
            )
                yield return name.Identifier;
        }
        if (_symbol is IParameterSymbol)
            foreach (
                var attribute in node.DescendantNodes(descendIntoTrivia: true)
                    .OfType<XmlNameAttributeSyntax>()
            )
                if (
                    attribute.Identifier.Identifier.ValueText == _symbol.Name
                    && attribute.Identifier.Identifier.Span is var span
                    && _symbol.ContainingSymbol.DeclaringSyntaxReferences.Any(reference =>
                        reference.SyntaxTree == attribute.SyntaxTree
                        && reference.GetSyntax().FullSpan.Contains(span)
                    )
                )
                    yield return attribute.Identifier.Identifier;
    }

    // Locals and parameters with equal display identity in different members are different symbols.
    private bool SameDeclaration(ISymbol bound) =>
        _symbol is not (ILocalSymbol or IParameterSymbol)
        || bound
            .DeclaringSyntaxReferences.Select(reference => (reference.SyntaxTree, reference.Span))
            .SequenceEqual(
                _symbol.DeclaringSyntaxReferences.Select(reference =>
                    (reference.SyntaxTree, reference.Span)
                )
            );

    private static SyntaxToken Identifier(SyntaxNode declaration) =>
        declaration.ChildTokens().FirstOrDefault(token => token.IsKind(SyntaxKind.IdentifierToken));

    private string? Validate(
        RewriteContext context,
        IReadOnlyList<SourceEdit> edits,
        (SyntaxTree Tree, Microsoft.CodeAnalysis.Text.TextSpan Span)[] declarations,
        bool local,
        Func<RenameChange, bool> proof
    )
    {
        var own = edits.Select(edit => edit.FileIdentity).ToHashSet();
        var sources = context
            .Original.Sources.Where(source => own.Contains(source.Document.FileIdentity))
            .ToArray();
        // The declaration in this compilation: the original one, or its linked copy.
        var declared = declarations
            .Select(declaration =>
                sources
                    .Where(source => source.Tree.FilePath == declaration.Tree.FilePath)
                    .Select(source =>
                        (
                            Source: source,
                            Node: source
                                .Tree.GetRoot(context.Original.CancellationToken)
                                .FindNode(declaration.Span, getInnermostNodeForTie: true)
                        )
                    )
                    .FirstOrDefault()
            )
            .FirstOrDefault(item => item.Source is not null);
        ISymbol? before = null;
        ISymbol? after = null;
        if (declared.Source is { } declaringSource)
        {
            before = declaringSource.Model.GetDeclaredSymbol(
                declared.Node,
                context.Original.CancellationToken
            );
            if (context.Map(declaringSource, declared.Node) is not { } mapped)
                return "the declaration has no counterpart after the edit";
            after = mapped.Model.GetDeclaredSymbol(
                mapped.After,
                context.Original.CancellationToken
            );
            if (before is null || after is null || after.Name != _name || after.Kind != before.Kind)
                return "the renamed declaration does not declare the renamed symbol";
        }
        else
        {
            // A compilation that only references the symbol, such as a dependent project.
            before = sources
                .SelectMany(source => Bound(source, context, edits))
                .Select(pair => pair.Before)
                .FirstOrDefault(symbol => symbol is not null);
            after = sources
                .SelectMany(source => Bound(source, context, edits))
                .Select(pair => pair.After)
                .FirstOrDefault(symbol => symbol is not null);
            if (before is null || after is null)
                return "the renamed references do not bind in this compilation";
        }
        var references = 0;
        foreach (var source in sources)
        {
            foreach (var (oldSymbol, newSymbol) in Bound(source, context, edits))
            {
                if (
                    newSymbol is null
                    || !SymbolEqualityComparer.Default.Equals(
                        newSymbol.OriginalDefinition,
                        after.OriginalDefinition
                    )
                )
                    return "a renamed reference does not bind to the renamed symbol";
                references++;
            }
            var scope = local
                ? Scope(source, before, local)
                    ?? [source.Tree.GetRoot(context.Original.CancellationToken)]
                : [source.Tree.GetRoot(context.Original.CancellationToken)];
            if (Unchanged(context, source, scope, before, after) is { } changed)
                return changed;
        }
        if (local is false)
            foreach (
                var other in context.Original.Sources.Where(source =>
                    !own.Contains(source.Document.FileIdentity)
                )
            )
                if (
                    Unchanged(
                        context,
                        other,
                        [other.Tree.GetRoot(context.Original.CancellationToken)],
                        before,
                        after
                    ) is
                    { } changed
                )
                    return changed;
        var evidence = new RewriteEvidence(
            context,
            new(
                sources[0],
                sources[0].Tree.GetRoot(),
                context.TreeFor(sources[0])!.GetRoot(),
                context.Rewritten.GetSemanticModel(context.TreeFor(sources[0])!)
            ),
            [],
            edits
        );
        if (RewriteChecks.CompilerSuppliedArguments(evidence, null) is { Holds: false } supplied)
            return supplied.Reason;
        return proof(new RenameChange(context, before, after, references))
            ? null
            : "the rule's SafeWhen proof returned false";
    }

    // Each renamed identifier that is a reference rather than a declaration, with its symbol before and after.
    private IEnumerable<(ISymbol? Before, ISymbol? After)> Bound(
        AnalysisSource source,
        RewriteContext context,
        IReadOnlyList<SourceEdit> edits
    )
    {
        var root = source.Tree.GetRoot(context.Original.CancellationToken);
        foreach (var edit in edits.Where(edit => edit.FileIdentity == source.Document.FileIdentity))
        {
            var token = root.FindToken(edit.Start);
            if (
                token.Parent is not IdentifierNameSyntax name
                || name.Parent is XmlNameAttributeSyntax
            )
                continue;
            var mapped = context.Map(source, name);
            yield return (
                Resolve(source.Model, name),
                mapped is { After: IdentifierNameSyntax renamed }
                    ? Resolve(mapped.Model, renamed)
                    : null
            );
        }
    }

    // A rename can only change what a name spelled like the old or new name binds to, and through it the
    // expressions containing that name, such as a call whose method group lost a member. The new name appears
    // nowhere in the searched source, so checking every old-name identifier and its enclosing expressions
    // covers every binding the rename can change.
    private static string? Unchanged(
        RewriteContext context,
        AnalysisSource source,
        IReadOnlyList<SyntaxNode> scope,
        ISymbol before,
        ISymbol after
    )
    {
        var expressions = scope
            .SelectMany(node =>
                node.DescendantNodesAndSelf(descendIntoTrivia: true).OfType<IdentifierNameSyntax>()
            )
            .Where(name =>
                name.Identifier.ValueText == before.Name || name.Identifier.ValueText == after.Name
            )
            .SelectMany(name =>
                name.AncestorsAndSelf()
                    .TakeWhile(node => node is ExpressionSyntax)
                    .Cast<ExpressionSyntax>()
            )
            .Distinct();
        foreach (var expression in expressions)
        {
            context.Original.CancellationToken.ThrowIfCancellationRequested();
            // nameof follows the symbol: its result is the new name, as a rename intends.
            if (
                IsNameof(source.Model, expression)
                || expression.Parent is XmlNameAttributeSyntax or CrefSyntax
            )
                continue;
            if (context.Map(source, expression) is not { After: ExpressionSyntax mapped } rewrite)
                return FixReasons.Describe(source, expression)
                    + " has no corresponding expression after the rename";
            var oldSymbol = Resolve(source.Model, expression);
            var newSymbol = Resolve(rewrite.Model, mapped);
            if (
                SymbolEqualityComparer.Default.Equals(
                    oldSymbol?.OriginalDefinition,
                    before.OriginalDefinition
                )
            )
            {
                if (
                    !SymbolEqualityComparer.Default.Equals(
                        newSymbol?.OriginalDefinition,
                        after.OriginalDefinition
                    )
                )
                    return FixReasons.Describe(source, expression)
                        + " no longer binds to the renamed symbol";
                continue;
            }
            if (
                !RewriteChecks.SameTypes(source.Model, expression, rewrite.Model, mapped)
                || !context.Same(oldSymbol, newSymbol)
            )
                return FixReasons.Describe(source, expression)
                    + " binds differently after the rename";
        }
        return null;
    }

    // A method named in nameof binds as a member group without a single symbol.
    private static ISymbol? Resolve(SemanticModel model, ExpressionSyntax expression)
    {
        var info = model.GetSymbolInfo(expression);
        return info.Symbol
            ?? (
                info.CandidateReason == CandidateReason.MemberGroup
                && info.CandidateSymbols.Length == 1
                    ? info.CandidateSymbols[0]
                    : null
            );
    }

    private static bool IsNameof(SemanticModel model, ExpressionSyntax expression) =>
        expression switch
        {
            InvocationExpressionSyntax { Expression: IdentifierNameSyntax name } =>
                name.Identifier.ValueText == "nameof"
                    && model.GetSymbolInfo(expression).Symbol is null,
            IdentifierNameSyntax
            {
                Identifier.ValueText: "nameof",
                Parent: InvocationExpressionSyntax invocation
            } => IsNameof(model, invocation),
            _ => false,
        };

    // Each compilation is validated against its references as they were, so a reference from another project
    // would keep seeing the old name. Linked files are fine: every compilation containing them is validated.
    private string? OutsideDeclaringProjects(IEnumerable<AnalysisSource> sources)
    {
        var declaring = _symbol!
            .DeclaringSyntaxReferences.Select(reference => reference.SyntaxTree.FilePath)
            .ToHashSet(StringComparer.Ordinal);
        var outside = sources.FirstOrDefault(source =>
            !source.Project.Sources.Any(candidate => declaring.Contains(candidate.Tree.FilePath))
        );
        return outside is null
            ? null
            : $"{outside.Document.FileIdentity} in {outside.Project.Name} references the symbol from another project, and project references are validated as they were";
    }
}
