using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

/// <summary>An immutable rename or move of one whole source file.</summary>
/// <remarks>
/// Gates: editable ordinary source in a project that selects its sources only by the SDK's default compile glob (no explicit, linked or removed Compile items and no exclusions the snapshot cannot read); a membership from that glob in every loaded project and target framework containing the file; a destination ending in <c>.cs</c>, not named like generated code, below the same project directories, outside excluded, <c>obj</c> and dot-named folders, not compiled by any other loaded project, and not the path of another loaded file; and, in every affected compilation, a compilation without errors. The file's text is unchanged, so every other binding is too.
/// The CLI also refuses at write time a destination that exists on disk, passes through a symbolic link or reparse point, or lies in a folder holding another project file, and after the move checks that the same projects compile the file.
/// </remarks>
public sealed class FileMove
{
    private const string CallerFilePath = "System.Runtime.CompilerServices.CallerFilePathAttribute";

    private readonly CodeFile _file;
    private readonly string? _destination;

    internal FileMove(CodeFile file, string? destination)
    {
        _file = file;
        _destination = destination;
    }

    /// <summary>Proposes the move when no call in the file receives a compiler-supplied caller file path, which would change with the path. The library owns this proof.</summary>
    public FixProposal? Propose() => Plan(null);

    /// <summary>Proposes the move when your proof holds in every affected compilation, for instance that the caller file paths it changes only locate snapshot files that move with it, or are logged.</summary>
    /// <param name="proof">States why the change preserves behavior, using the methods that receive the changed caller file path.</param>
    public FixProposal? SafeWhen(Func<FileMoveChange, bool> proof) => Plan(proof);

    private FixProposal Plan(Func<FileMoveChange, bool>? proof)
    {
        var source = _file.Source;
        if (!source.Document.IsEditable || source.Document.IsGenerated)
            return FixProposal.Withheld("the file is generated or not editable");
        if (_destination is null)
            return FixProposal.Withheld(
                "the project does not select its sources only by the SDK's default compile glob"
            );
        var projects =
            source.Project.SolutionOrNull?.Projects.Select(project => project.Snapshot).ToArray()
            ?? [source.Project.Snapshot];
        if (
            FileMovePolicy.Check(projects, source.Document.FileIdentity, _destination) is
            { } unsupported
        )
            return FixProposal.Withheld(unsupported);
        var identity = source.Document.FileIdentity;
        var from = source.Document.Path;
        var to = _destination;
        return new FixProposal(
            [],
            [new SourceMove(identity, source.Document.Fingerprint, to)],
            (project, combined) => Validate(project, combined, identity, from, to, proof)
        );
    }

    private static string? Validate(
        AnalysisProject project,
        IReadOnlyList<SourceEdit> combined,
        string identity,
        string from,
        string to,
        Func<FileMoveChange, bool>? proof
    )
    {
        var memberships = project
            .Sources.Where(source => source.Document.FileIdentity == identity)
            .ToArray();
        if (memberships.Length == 0)
            return "the moved file is not part of this compilation";
        var prepared = SourceChanges.Rewrite(project, combined);
        if (prepared.Context is not { } context)
            return prepared.Reason;
        foreach (var source in memberships)
        {
            if (InitializationOrder(source) is { } order)
                return order;
            var tree = context.TreeFor(source) ?? source.Tree;
            var methods = CallerFilePathMethods(
                    context.Rewritten.GetSemanticModel(tree),
                    project.CancellationToken
                )
                .ToArray();
            if (proof is null)
            {
                if (methods.Length > 0)
                    return $"the move changes the caller file path passed to {methods[0].ToDisplayString()}; use SafeWhen";
            }
            else if (!proof(new FileMoveChange(source, from, to, methods, context)))
                return "the rule's SafeWhen proof returned false";
        }
        return null;
    }

    // The compiler orders partial declarations and module initializers by file order, which a move can change.
    private static string? InitializationOrder(AnalysisSource source)
    {
        var token = source.Project.CancellationToken;
        var root = source.Tree.GetRoot(token);
        if (
            root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Any(method =>
                    method.AttributeLists.Count > 0
                    && source
                        .Model.GetDeclaredSymbol(method, token)
                        ?.GetAttributes()
                        .Any(attribute =>
                            attribute.AttributeClass?.ToDisplayString()
                            == "System.Runtime.CompilerServices.ModuleInitializerAttribute"
                        ) == true
                )
        )
            return "the move may reorder the module initializers it declares";
        var partial = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(type =>
                type.Modifiers.Any(SyntaxKind.PartialKeyword)
                && type.Members.Any(member =>
                    member is PropertyDeclarationSyntax { Initializer: not null }
                    || member is FieldDeclarationSyntax field
                        && field.Declaration.Variables.Any(variable =>
                            variable.Initializer is not null
                        )
                )
                && source.Model.GetDeclaredSymbol(type, token)
                    is { DeclaringSyntaxReferences.Length: > 1 } symbol
                && symbol.DeclaringSyntaxReferences.Any(reference =>
                    reference.SyntaxTree != source.Tree
                )
            );
        return partial is null
            ? null
            : $"the move may reorder the field initializers of partial {partial.Identifier.Text} across its files";
    }

    // Calls in the file whose omitted [CallerFilePath] argument the compiler fills with the file's path,
    // including implicit calls: collection initializer Add calls, conditional indexers, implicit base
    // constructor calls and the pattern methods of foreach, await and deconstruction.
    // Attributes are left out: their caller file path is metadata saying where the code is.
    private static IEnumerable<IMethodSymbol> CallerFilePathMethods(
        SemanticModel model,
        CancellationToken cancellationToken
    )
    {
        var nodes = model
            .SyntaxTree.GetRoot(cancellationToken)
            .DescendantNodes(node => node is not AttributeListSyntax)
            .ToArray();
        var explicitCalls = nodes
            .Select(node => model.GetOperation(node, cancellationToken))
            .Where(operation => operation is { Parent: null })
            .SelectMany(operation => operation!.DescendantsAndSelf())
            .SelectMany(operation =>
                operation switch
                {
                    IInvocationOperation call => call.Arguments.Select(argument =>
                        (Method: call.TargetMethod, Argument: argument)
                    ),
                    IObjectCreationOperation { Constructor: { } constructor } creation =>
                        creation.Arguments.Select(argument =>
                            (Method: constructor, Argument: argument)
                        ),
                    IPropertyReferenceOperation { Property: var property } indexer
                        when (property.GetMethod ?? property.SetMethod) is { } accessor =>
                        indexer.Arguments.Select(argument =>
                            (Method: accessor, Argument: argument)
                        ),
                    _ => Enumerable.Empty<(IMethodSymbol Method, IArgumentOperation Argument)>(),
                }
            )
            .Where(item =>
                item.Argument.ArgumentKind == ArgumentKind.DefaultValue
                && IsCallerFilePath(item.Argument.Parameter)
            )
            .Select(item => item.Method);
        var implicitCalls = nodes
            .SelectMany(node => ImplicitCalls(model, node, cancellationToken))
            .Where(method => method.Parameters.Any(IsCallerFilePath));
        return explicitCalls
            .Concat(implicitCalls)
            .Distinct(SymbolEqualityComparer.Default)
            .Cast<IMethodSymbol>();
    }

    // Methods the compiler calls without written arguments, so every caller-info parameter gets the file path.
    private static IEnumerable<IMethodSymbol> ImplicitCalls(
        SemanticModel model,
        SyntaxNode node,
        CancellationToken cancellationToken
    ) =>
        (
            node switch
            {
                CommonForEachStatementSyntax loop => model.GetForEachStatementInfo(loop) is var info
                    ?
                    [
                        info.GetEnumeratorMethod,
                        info.MoveNextMethod,
                        info.CurrentProperty?.GetMethod,
                        info.DisposeMethod,
                    ]
                    : [],
                AwaitExpressionSyntax await => model.GetAwaitExpressionInfo(await) is var info
                    ?
                    [
                        info.GetAwaiterMethod,
                        info.GetResultMethod,
                        info.IsCompletedProperty?.GetMethod,
                    ]
                    : [],
                AssignmentExpressionSyntax
                {
                    Left: TupleExpressionSyntax or DeclarationExpressionSyntax,
                } deconstruction => Deconstructions(model.GetDeconstructionInfo(deconstruction)),
                ConstructorDeclarationSyntax { Initializer: null } constructor
                    when !constructor.Modifiers.Any(modifier => modifier.ValueText == "static") =>
                    BaseConstructors(
                        model.GetDeclaredSymbol(constructor, cancellationToken)?.ContainingType
                    ),
                TypeDeclarationSyntax type
                    when type.BaseList?.Types.FirstOrDefault()
                        is not PrimaryConstructorBaseTypeSyntax
                        && model.GetDeclaredSymbol(type, cancellationToken) is { } symbol
                        && symbol.InstanceConstructors.Any(constructor =>
                            constructor.IsImplicitlyDeclared
                            || constructor.DeclaringSyntaxReferences.Any(reference =>
                                reference.GetSyntax(cancellationToken) == type
                            )
                        ) => BaseConstructors(symbol),
                _ => Array.Empty<IMethodSymbol?>(),
            }
        ).OfType<IMethodSymbol>();

    private static IEnumerable<IMethodSymbol?> Deconstructions(DeconstructionInfo info) =>
        info.Nested.SelectMany(Deconstructions).Prepend(info.Method);

    // The base constructors a constructor without a written initializer may call: those callable without
    // arguments.
    private static IEnumerable<IMethodSymbol> BaseConstructors(INamedTypeSymbol? type) =>
        type is { TypeKind: TypeKind.Class, BaseType: { } baseType }
            ? baseType.InstanceConstructors.Where(constructor =>
                constructor.Parameters.All(parameter => parameter.IsOptional || parameter.IsParams)
            )
            : [];

    private static bool IsCallerFilePath(IParameterSymbol? parameter) =>
        parameter
            ?.GetAttributes()
            .Any(attribute => attribute.AttributeClass?.ToDisplayString() == CallerFilePath)
        == true;
}
