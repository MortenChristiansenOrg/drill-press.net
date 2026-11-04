using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

// The members each function in one document runs, read from the document's shared operations so that call paths
// bind nothing other queries don't. Lambdas and anonymous methods belong to the function that creates them, with
// deferred sites; local functions are separate functions, run only where they are called. Instance field, property
// and event initializers belong to the type declaration whose constructors run them.
internal sealed class DocumentCallSites
{
    private readonly Dictionary<TextSpan, List<CallSite>> _functions = [];
    private readonly Dictionary<TextSpan, List<CallSite>> _initializers = [];

    public DocumentCallSites(AnalysisSource source)
    {
        var token = source.Project.CancellationToken;
        foreach (var operation in source.Operations.All)
        {
            token.ThrowIfCancellationRequested();
            switch (operation)
            {
                case IInvocationOperation call:
                    Add(
                        source,
                        call,
                        NameSpan(call.Syntax),
                        call.TargetMethod,
                        false,
                        Dispatchable(call.Instance, call.TargetMethod)
                    );
                    break;
                case IObjectCreationOperation { Constructor: { } constructor } creation:
                    Add(source, creation, creation.Syntax.Span, constructor, false, false);
                    break;
                case IPropertyReferenceOperation property:
                    foreach (var accessor in Accessors(property))
                        Add(
                            source,
                            property,
                            NameSpan(property.Syntax),
                            accessor,
                            false,
                            Dispatchable(property.Instance, accessor)
                        );
                    break;
                case IEventAssignmentOperation
                {
                    EventReference: IEventReferenceOperation reference
                } assignment:
                    if (
                        (
                            assignment.Adds
                                ? reference.Event.AddMethod
                                : reference.Event.RemoveMethod
                        ) is
                        { } eventAccessor
                    )
                        Add(
                            source,
                            assignment,
                            NameSpan(reference.Syntax),
                            eventAccessor,
                            false,
                            Dispatchable(reference.Instance, eventAccessor)
                        );
                    break;
                case IMethodReferenceOperation group:
                    Add(source, group, NameSpan(group.Syntax), group.Method, true, group.IsVirtual);
                    break;
                default:
                    if (OperatorOf(operation) is { } method)
                        Add(source, operation, operation.Syntax.Span, method, false, false);
                    break;
            }
        }
    }

    // The sites in a function's own body, or none for a function without one.
    public IReadOnlyList<CallSite> Of(SyntaxNode function) =>
        _functions.GetValueOrDefault(function.Span) ?? [];

    // The instance member initializers in one part of a type.
    public IReadOnlyList<CallSite> InitializersOf(TypeDeclarationSyntax type) =>
        _initializers.GetValueOrDefault(type.Span) ?? [];

    private void Add(
        AnalysisSource source,
        IOperation operation,
        TextSpan span,
        IMethodSymbol target,
        bool deferred,
        bool dispatchable
    )
    {
        if (Owner(operation, ref deferred) is not var (owner, initializer))
            return;
        var index = initializer ? _initializers : _functions;
        if (!index.TryGetValue(owner, out var sites))
        {
            sites = [];
            index.Add(owner, sites);
        }

        sites.Add(new(source, span, target, deferred, dispatchable));
    }

    // The function or type declaration a site belongs to, found through its enclosing operations. Sites in nameof,
    // attributes, parameter defaults, static initializers and top-level statements have none.
    private static (TextSpan Owner, bool Initializer)? Owner(
        IOperation operation,
        ref bool deferred
    )
    {
        for (var current = operation.Parent; ; current = current.Parent)
        {
            switch (current)
            {
                case null:
                    return null;
                case INameOfOperation:
                    return null;
                case IAnonymousFunctionOperation:
                    deferred = true;
                    break;
                case ILocalFunctionOperation or IMethodBodyBaseOperation:
                    return (current.Syntax.Span, false);
                case IFieldInitializerOperation field:
                    return field.InitializedFields.Any(member => member.IsStatic || member.IsConst)
                        ? null
                        : InitializerOwner(field);
                case IPropertyInitializerOperation property:
                    return property.InitializedProperties.Any(member => member.IsStatic)
                        ? null
                        : InitializerOwner(property);
                case { Parent: null, Syntax: ArrowExpressionClauseSyntax arrow }:
                    return (arrow.Span, false);
            }
        }
    }

    private static (TextSpan, bool)? InitializerOwner(IOperation initializer) =>
        initializer.Syntax.FirstAncestorOrSelf<TypeDeclarationSyntax>() is { } type
            ? (type.Span, true)
            : null;

    private static IMethodSymbol? OperatorOf(IOperation operation) =>
        operation switch
        {
            IBinaryOperation binary => binary.OperatorMethod,
            IUnaryOperation unary => unary.OperatorMethod,
            IConversionOperation conversion => conversion.OperatorMethod,
            ICompoundAssignmentOperation compound => compound.OperatorMethod,
            IIncrementOrDecrementOperation increment => increment.OperatorMethod,
            _ => null,
        };

    // Reads use the getter, assignments the setter or init accessor, and compound updates both.
    private static IEnumerable<IMethodSymbol> Accessors(IPropertyReferenceOperation reference)
    {
        var property = reference.Property;
        var (reads, writes) = reference.Parent switch
        {
            ISimpleAssignmentOperation assignment when assignment.Target == reference => (
                false,
                true
            ),
            ICompoundAssignmentOperation compound when compound.Target == reference => (true, true),
            ICoalesceAssignmentOperation coalesce when coalesce.Target == reference => (true, true),
            IIncrementOrDecrementOperation increment when increment.Target == reference => (
                true,
                true
            ),
            ITupleOperation tuple when IsDeconstructionTarget(tuple) => (false, true),
            _ => (true, false),
        };
        if (reads && property.GetMethod is { } getter)
            yield return getter;
        if (writes && property.SetMethod is { } setter)
            yield return setter;
    }

    private static bool IsDeconstructionTarget(ITupleOperation tuple)
    {
        IOperation current = tuple;
        while (current.Parent is ITupleOperation parent)
            current = parent;
        return current.Parent is IDeconstructionAssignmentOperation assignment
            && assignment.Target == current;
    }

    // Calls through base and static members other than interface abstractions have one implementation.
    private static bool Dispatchable(IOperation? instance, IMethodSymbol target)
    {
        if (target.IsStatic)
            return target.ContainingType.TypeKind == TypeKind.Interface
                && (target.IsAbstract || target.IsVirtual);
        if (instance?.Syntax is BaseExpressionSyntax)
            return false;
        return target.IsAbstract || target.IsVirtual || target.IsOverride && !target.IsSealed;
    }

    // The member name, so chained calls and accesses have distinct locations.
    internal static TextSpan NameSpan(SyntaxNode syntax) =>
        syntax switch
        {
            InvocationExpressionSyntax { Expression: var callee } => NameSpan(callee),
            MemberAccessExpressionSyntax access => access.Name.Span,
            MemberBindingExpressionSyntax binding => binding.Name.Span,
            AliasQualifiedNameSyntax alias => alias.Name.Span,
            _ => syntax.Span,
        };
}

// Assembles one function's sites: a constructor first runs its type's member initializers, unless it chains to
// this(...), then its base or this initializer, then its body.
internal sealed class CallSiteDiscovery(CallGraph graph, CodeFunction function)
{
    private readonly List<CallSite> _sites = [];
    private readonly CancellationToken _token = function.Source.Project.CancellationToken;

    public IReadOnlyList<CallSite> Discover()
    {
        var source = function.Source;
        switch (function.Syntax)
        {
            case ConstructorDeclarationSyntax constructor:
                if (!constructor.Initializer.IsKind(SyntaxKind.ThisConstructorInitializer))
                    MemberInitializers();
                _sites.AddRange(graph.SitesIn(source).Of(constructor));
                break;
            case TypeDeclarationSyntax type:
                MemberInitializers();
                if (
                    !function.IsImplicit
                    && type.BaseList?.Types.FirstOrDefault()
                        is PrimaryConstructorBaseTypeSyntax primaryBase
                    && source.Model.GetOperation(primaryBase, _token)
                        is IInvocationOperation baseCall
                )
                    _sites.Add(
                        new(
                            source,
                            DocumentCallSites.NameSpan(primaryBase),
                            baseCall.TargetMethod,
                            false,
                            false
                        )
                    );
                else
                    ImplicitBase(type);
                break;
            default:
                return graph.SitesIn(source).Of(function.Syntax);
        }

        return _sites.AsReadOnly();
    }

    private void MemberInitializers()
    {
        if (function.Symbol?.ContainingType is not { } type)
            return;
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (
                reference.GetSyntax(_token) is TypeDeclarationSyntax declaration
                && graph.SourceOf(reference.SyntaxTree, type) is { } source
            )
                _sites.AddRange(graph.SitesIn(source).InitializersOf(declaration));
        }
    }

    // A constructor without a written initializer calls the base type's parameterless constructor.
    private void ImplicitBase(TypeDeclarationSyntax type)
    {
        if (
            function.Symbol?.ContainingType
                is not { TypeKind: TypeKind.Class, BaseType: { } baseType }
            || baseType.SpecialType == SpecialType.System_Object
        )
            return;
        var candidates = baseType
            .InstanceConstructors.Where(constructor =>
                constructor.Parameters.All(parameter => parameter.IsOptional || parameter.IsParams)
            )
            .ToArray();
        var target =
            candidates.Length == 1
                ? candidates[0]
                : candidates.SingleOrDefault(constructor => constructor.Parameters.IsEmpty);
        if (target is not null)
            _sites.Add(new(function.Source, type.Identifier.Span, target, false, false));
    }
}
