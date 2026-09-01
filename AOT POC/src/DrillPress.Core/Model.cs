using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

public interface ICodeEntity
{
    SourceLocation Location { get; }
}

public sealed record SourceLocation(SourceDocument Document, TextSpan Span)
{
    public static SourceLocation At(SyntaxNode node, SourceDocument document) => new(document, node.Span);
}

public sealed class SourceDocument
{
    private SemanticModel? _semanticModel;

    internal SourceDocument(ProjectModel project, string path, SyntaxTree syntaxTree, bool isGenerated = false)
    {
        Project = project;
        Path = path;
        SyntaxTree = syntaxTree;
        IsGenerated = isGenerated;
    }

    public ProjectModel Project { get; }

    public string Path { get; }

    public SyntaxTree SyntaxTree { get; }

    public bool IsGenerated { get; }

    public SourceText Text => SyntaxTree.GetText();

    public SyntaxNode Root => SyntaxTree.GetRoot();

    public SemanticModel SemanticModel => _semanticModel ??= Project.Compilation.GetSemanticModel(SyntaxTree);
}

public sealed class ProjectModel
{
    private ImmutableArray<MethodModel> _methods;
    private ImmutableArray<InterfaceModel> _interfaces;
    private ImmutableArray<NamedTypeModel> _types;
    private ImmutableArray<MemberReferenceModel> _memberReferences;
    private readonly Dictionary<string, ImmutableArray<MemberReferenceModel>> _memberReferencesByName =
        new(StringComparer.Ordinal);

    internal ProjectModel(string name, string path, bool isTestProject)
    {
        Name = name;
        Path = path;
        IsTestProject = isTestProject;
    }

    public string Name { get; }

    public string Path { get; }

    public bool IsTestProject { get; }

    public CSharpCompilation Compilation { get; internal set; } = null!;

    public ImmutableArray<SourceDocument> Documents { get; internal set; } = [];

    internal AnalysisSolution Solution { get; set; } = null!;

    internal ImmutableArray<MethodModel> Methods =>
        !_methods.IsDefault ? _methods : _methods = DiscoverMethods();

    internal ImmutableArray<InterfaceModel> Interfaces =>
        !_interfaces.IsDefault ? _interfaces : _interfaces = DiscoverInterfaces();

    internal ImmutableArray<NamedTypeModel> Types =>
        !_types.IsDefault ? _types : _types = DiscoverTypes();

    internal void PrepareMemberReferences(ImmutableHashSet<string>? requiredNames)
    {
        if (!_memberReferences.IsDefault ||
            requiredNames is not null && requiredNames.All(_memberReferencesByName.ContainsKey))
        {
            return;
        }

        DiscoverMemberReferences(requiredNames);
    }

    internal IEnumerable<MemberReferenceModel> GetMemberReferences(ImmutableHashSet<string>? requiredNames)
    {
        PrepareMemberReferences(requiredNames);
        if (requiredNames is null)
        {
            return _memberReferences;
        }

        return requiredNames.SelectMany(name => _memberReferencesByName[name]);
    }

    private ImmutableArray<MethodModel> DiscoverMethods()
    {
        var result = ImmutableArray.CreateBuilder<MethodModel>();
        foreach (var document in Documents)
        {
            foreach (var declaration in document.Root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (document.SemanticModel.GetDeclaredSymbol(declaration) is IMethodSymbol symbol)
                {
                    result.Add(new MethodModel(document, declaration, symbol));
                }
            }
        }

        return result.ToImmutable();
    }

    private ImmutableArray<InterfaceModel> DiscoverInterfaces()
    {
        var result = ImmutableArray.CreateBuilder<InterfaceModel>();
        foreach (var document in Documents)
        {
            foreach (var declaration in document.Root.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
            {
                if (document.SemanticModel.GetDeclaredSymbol(declaration) is INamedTypeSymbol symbol)
                {
                    result.Add(new InterfaceModel(document, declaration, symbol));
                }
            }
        }

        return result.ToImmutable();
    }

    private ImmutableArray<NamedTypeModel> DiscoverTypes()
    {
        var result = ImmutableArray.CreateBuilder<NamedTypeModel>();
        foreach (var document in Documents)
        {
            foreach (var declaration in document.Root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (document.SemanticModel.GetDeclaredSymbol(declaration) is INamedTypeSymbol symbol)
                {
                    result.Add(new NamedTypeModel(document, declaration, symbol));
                }
            }
        }

        return result.ToImmutable();
    }

    private void DiscoverMemberReferences(ImmutableHashSet<string>? requiredNames)
    {
        var result = ImmutableArray.CreateBuilder<MemberReferenceModel>();
        var byName = requiredNames?.ToDictionary(
            static name => name,
            static _ => ImmutableArray.CreateBuilder<MemberReferenceModel>(),
            StringComparer.Ordinal);
        foreach (var document in Documents)
        {
            foreach (var identifier in document.Root.DescendantNodes().OfType<SimpleNameSyntax>())
            {
                var name = identifier.Identifier.ValueText;
                if (requiredNames is not null && !requiredNames.Contains(name))
                {
                    continue;
                }

                var expression = GetCompleteMemberReference(identifier);
                if (expression is null)
                {
                    continue;
                }

                var symbolInfo = document.SemanticModel.GetSymbolInfo(expression);
                var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
                if (symbol is IFieldSymbol or IPropertySymbol or IMethodSymbol)
                {
                    var memberReference = new MemberReferenceModel(document, expression, symbol);
                    result.Add(memberReference);
                    byName?[name].Add(memberReference);
                }
            }
        }

        if (requiredNames is null)
        {
            _memberReferences = result.ToImmutable();
            return;
        }

        foreach (var (name, references) in byName!)
        {
            _memberReferencesByName[name] = references.ToImmutable();
        }
    }

    private static ExpressionSyntax? GetCompleteMemberReference(SimpleNameSyntax identifier)
    {
        if (identifier.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == identifier)
        {
            return memberAccess;
        }

        return identifier is not IdentifierNameSyntax
            ? null
            : identifier.Parent switch
            {
                QualifiedNameSyntax => null,
                AliasQualifiedNameSyntax => null,
                _ => identifier,
            };
    }
}

public sealed class AnalysisSolution
{
    private Dictionary<ISymbol, ImmutableArray<NamedTypeModel>>? _implementationsByInterface;

    internal AnalysisSolution(ImmutableArray<ProjectModel> projects)
    {
        Projects = projects;
        foreach (var project in projects)
        {
            project.Solution = this;
        }
    }

    public ImmutableArray<ProjectModel> Projects { get; }

    public IEnumerable<MethodModel> Methods => Projects.SelectMany(static project => project.Methods);

    public IEnumerable<InterfaceModel> Interfaces => Projects.SelectMany(static project => project.Interfaces);

    public IEnumerable<NamedTypeModel> Types => Projects.SelectMany(static project => project.Types);

    public IEnumerable<MemberReferenceModel> MemberReferences => GetMemberReferences(null);

    internal void PrepareMemberReferences(ImmutableHashSet<string>? requiredNames)
    {
        foreach (var project in Projects)
        {
            project.PrepareMemberReferences(requiredNames);
        }
    }

    internal IEnumerable<MemberReferenceModel> GetMemberReferences(ImmutableHashSet<string>? requiredNames) =>
        Projects.SelectMany(project => project.GetMemberReferences(requiredNames));

    internal IEnumerable<NamedTypeModel> GetNonTestConcreteImplementations(INamedTypeSymbol interfaceSymbol)
    {
        _implementationsByInterface ??= BuildImplementationIndex();
        return _implementationsByInterface.GetValueOrDefault(interfaceSymbol, []);
    }

    private Dictionary<ISymbol, ImmutableArray<NamedTypeModel>> BuildImplementationIndex()
    {
        var builders = new Dictionary<ISymbol, ImmutableArray<NamedTypeModel>.Builder>(SymbolEqualityComparer.Default);
        foreach (var type in Types.Where(static type =>
                     !type.Document.Project.IsTestProject &&
                     type.Symbol.TypeKind == TypeKind.Class &&
                     !type.Symbol.IsAbstract))
        {
            foreach (var @interface in type.Symbol.AllInterfaces)
            {
                if (!builders.TryGetValue(@interface, out var implementations))
                {
                    implementations = ImmutableArray.CreateBuilder<NamedTypeModel>();
                    builders.Add(@interface, implementations);
                }

                implementations.Add(type);
            }
        }

        return builders.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToImmutable(),
            SymbolEqualityComparer.Default);
    }
}

public sealed class MethodModel(SourceDocument document, MethodDeclarationSyntax declaration, IMethodSymbol symbol)
    : ICodeEntity
{
    private ImmutableArray<SourceLocation> _emptyLines;
    private ImmutableArray<AssertionModel> _assertions;

    public SourceDocument Document { get; } = document;

    public MethodDeclarationSyntax Declaration { get; } = declaration;

    public IMethodSymbol Symbol { get; } = symbol;

    public SourceLocation Location => new(Document, Declaration.Identifier.Span);

    public ImmutableArray<SourceLocation> EmptyLines =>
        !_emptyLines.IsDefault ? _emptyLines : _emptyLines = FindEmptyLines();

    public ImmutableArray<AssertionModel> Assertions =>
        !_assertions.IsDefault ? _assertions : _assertions = FindAssertions();

    private ImmutableArray<SourceLocation> FindEmptyLines()
    {
        if (Declaration.Body is null)
        {
            return [];
        }

        var text = Document.Text;
        var firstLine = text.Lines.GetLineFromPosition(Declaration.Body.OpenBraceToken.Span.End).LineNumber + 1;
        var lastLine = text.Lines.GetLineFromPosition(Declaration.Body.CloseBraceToken.SpanStart).LineNumber - 1;
        if (lastLine < firstLine)
        {
            return [];
        }

        var result = ImmutableArray.CreateBuilder<SourceLocation>();
        for (var lineNumber = firstLine; lineNumber <= lastLine; lineNumber++)
        {
            var line = text.Lines[lineNumber];
            if (string.IsNullOrWhiteSpace(line.ToString()))
            {
                result.Add(new SourceLocation(Document, line.Span));
            }
        }

        return result.ToImmutable();
    }

    private ImmutableArray<AssertionModel> FindAssertions()
    {
        if (Declaration.Body is null)
        {
            return [];
        }

        var result = ImmutableArray.CreateBuilder<AssertionModel>();
        foreach (var invocation in Declaration.Body.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var symbolInfo = Document.SemanticModel.GetSymbolInfo(invocation);
            if ((symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()) is IMethodSymbol method &&
                SymbolNames.FullName(method.ContainingType) == "Xunit.Assert")
            {
                result.Add(new AssertionModel(Document, invocation, method));
            }
        }

        return result.ToImmutable();
    }
}

public sealed class AssertionModel(
    SourceDocument document,
    InvocationExpressionSyntax syntax,
    IMethodSymbol symbol) : ICodeEntity
{
    public SourceDocument Document { get; } = document;

    public InvocationExpressionSyntax Syntax { get; } = syntax;

    public IMethodSymbol Symbol { get; } = symbol;

    public SourceLocation Location => SourceLocation.At(Syntax, Document);
}

public sealed class InterfaceModel(
    SourceDocument document,
    InterfaceDeclarationSyntax declaration,
    INamedTypeSymbol symbol) : ICodeEntity
{
    public SourceDocument Document { get; } = document;

    public InterfaceDeclarationSyntax Declaration { get; } = declaration;

    public INamedTypeSymbol Symbol { get; } = symbol;

    public SourceLocation Location => new(Document, Declaration.Identifier.Span);

    public IEnumerable<NamedTypeModel> NonTestConcreteImplementations =>
        Document.Project.Solution.GetNonTestConcreteImplementations(Symbol);
}

public sealed class NamedTypeModel(
    SourceDocument document,
    TypeDeclarationSyntax declaration,
    INamedTypeSymbol symbol) : ICodeEntity
{
    public SourceDocument Document { get; } = document;

    public TypeDeclarationSyntax Declaration { get; } = declaration;

    public INamedTypeSymbol Symbol { get; } = symbol;

    public SourceLocation Location => new(Document, Declaration.Identifier.Span);
}

public sealed class MemberReferenceModel(
    SourceDocument document,
    ExpressionSyntax syntax,
    ISymbol symbol) : ICodeEntity
{
    public SourceDocument Document { get; } = document;

    public ExpressionSyntax Syntax { get; } = syntax;

    public ISymbol Symbol { get; } = symbol;

    public SourceLocation Location => SourceLocation.At(Syntax, Document);
}

internal static class SymbolNames
{
    public static string FullName(INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return string.Empty;
        }

        var containing = type.ContainingType is not null
            ? FullName(type.ContainingType)
            : type.ContainingNamespace is { IsGlobalNamespace: false } ns
                ? ns.ToDisplayString()
                : string.Empty;
        return string.IsNullOrEmpty(containing) ? type.MetadataName : $"{containing}.{type.MetadataName}";
    }
}
