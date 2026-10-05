using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

/// <summary>A source expression and its compiler evidence; no expression evaluation or runtime inference is performed.</summary>
public sealed class CodeExpression(AnalysisSource source, ExpressionSyntax syntax) : ICodeElement
{
    /// <summary>Views an explicit bound invocation through parentheses and implicit built-in conversions. Explicit/user conversions and conditional-access envelopes remain unavailable.</summary>
    public CodeInvocation? AsInvocation()
    {
        if (IsImplicit || !IsResolved)
            return null;
        var operation = Operation;
        while (
            operation
                is IConversionOperation { IsImplicit: true, Conversion.IsUserDefined: false }
                    or IParenthesizedOperation
        )
            operation = operation is IConversionOperation conversion
                ? conversion.Operand
                : ((IParenthesizedOperation)operation).Operand;
        return
            operation is IInvocationOperation { IsImplicit: false } invocation
            && invocation.Syntax is ExpressionSyntax
            ? new(Source, invocation)
            : null;
    }

    /// <summary>Facts about enclosing observable syntax and interior trivia, independent of rewrite authorization.</summary>
    public ExpressionSourceFacts Facts => new(Source, Syntax);

    /// <summary>Matches a directly referenced member through parentheses and implicit built-in conversions; the original Operation and Conversion remain available.</summary>
    public bool RefersTo(CodeMember member) =>
        IsResolved
        && Operation is { } operation
        && ReferencedSymbol(operation) is { } symbol
        && member.Matches(symbol);

    /// <summary>Matches this expression's original static type.</summary>
    public bool TypeIs<T>() => TypeIs(CodeType.Of<T>());

    /// <summary>Matches a typed compiler constant, including a present null.</summary>
    public bool IsConstant<T>(T value) => Is(value);

    /// <summary>The compile-time string value, or null when unavailable or a constant null.</summary>
    public string? TextValue => ValueAs<string>() is { HasValue: true } value ? value.Value : null;

    /// <summary>Reads a typed compiler constant, preserving absence separately from zero, false or null.</summary>
    public Optional<T> ValueAs<T>() => CompilerConstant.Read<T>(Constant, Type);

    /// <summary>Tests a typed constant; enum comparisons require the same enum identity.</summary>
    public bool Is<T>(T value) =>
        ValueAs<T>() is { HasValue: true } actual
        && EqualityComparer<T>.Default.Equals(actual.Value, value);

    /// <summary>Tests constant string contents, including nameof results.</summary>
    public bool IsText(string text) => Is(text);

    private IOperation? _implicitReceiver;
    private readonly Lazy<bool> _resolved = new(() =>
        BoundOperation(source, syntax) is { Kind: not OperationKind.Invalid }
        && source.Model.GetTypeInfo(syntax, source.Project.CancellationToken).Type?.TypeKind
            != TypeKind.Error
        && !source
            .Model.GetDiagnostics(syntax.Span, source.Project.CancellationToken)
            .Any(d => d.Severity == DiagnosticSeverity.Error)
    );

    internal static CodeExpression ImplicitReceiver(
        AnalysisSource source,
        ExpressionSyntax anchor,
        IOperation receiver
    ) => new(source, anchor) { _implicitReceiver = receiver };

    /// <summary>Whether this view represents an implicit this receiver anchored to its call rather than an editable expression.</summary>
    public bool IsImplicit => _implicitReceiver is not null;

    /// <summary>The compilation membership supplying this expression's evidence.</summary>
    public AnalysisSource Source { get; } = source;

    /// <summary>The original expression, including explicit casts and parentheses.</summary>
    public ExpressionSyntax Syntax { get; } = syntax;

    /// <summary>The expression's reportable span, excluding exterior trivia.</summary>
    public SourceLocation Location => Source.Locate(Syntax.Span);

    /// <summary>The bound operation, absent for unsupported syntax.</summary>
    public IOperation? Operation => _implicitReceiver ?? BoundOperation(Source, Syntax);

    /// <summary>The selected symbol, excluding ambiguous candidate symbols.</summary>
    public ISymbol? Symbol =>
        IsImplicit
            ? null
            : Source.Model.GetSymbolInfo(Syntax, Source.Project.CancellationToken).Symbol;

    /// <summary>Original and converted types, including position-specific nullable evidence.</summary>
    public TypeInfo TypeInfo =>
        IsImplicit ? default : Source.Model.GetTypeInfo(Syntax, Source.Project.CancellationToken);

    /// <summary>The original static type, also available for an implicit this receiver without source syntax.</summary>
    public ITypeSymbol? Type => _implicitReceiver?.Type ?? TypeInfo.Type;

    /// <summary>The declaration annotation where the expression binds to a value declaration; None means no declaration evidence.</summary>
    public NullableAnnotation DeclaredNullability =>
        Symbol switch
        {
            ILocalSymbol local => local.NullableAnnotation,
            IParameterSymbol parameter => parameter.NullableAnnotation,
            IFieldSymbol member => member.NullableAnnotation,
            IPropertySymbol property => property.NullableAnnotation,
            IMethodSymbol method => method.ReturnNullableAnnotation,
            _ => NullableAnnotation.None,
        };

    /// <summary>The expression's compile-time constant; HasValue distinguishes a constant null from no constant.</summary>
    public Optional<object?> Constant =>
        IsImplicit
            ? default
            : Source.Model.GetConstantValue(Syntax, Source.Project.CancellationToken);

    /// <summary>The implicit conversion into the expression's enclosing context.</summary>
    public Conversion Conversion =>
        IsImplicit ? default : Source.Model.GetConversion(Syntax, Source.Project.CancellationToken);

    /// <summary>Whether binding is free of errors in this expression. Warnings do not establish runtime guarantees.</summary>
    public bool IsResolved => _resolved.Value;

    /// <summary>Matches the original static type, before conversion to an argument parameter.</summary>
    public bool TypeIs(CodeType type) => IsResolved && Type is { } actual && type.Matches(actual);

    /// <summary>Matches the target type after contextual conversion.</summary>
    public bool ConvertedTypeIs(CodeType type) =>
        IsResolved && TypeInfo.ConvertedType is { } actual && type.Matches(actual);

    /// <summary>Tests nominal base-class inheritance, preserving the receiver's actual static type.</summary>
    public bool TypeIsOrDerivesFrom(CodeType type) =>
        IsResolved && Type is INamedTypeSymbol named && Symbols.IsOrDerivesFrom(named, type);

    /// <summary>Tests identity or implicit reference conversion to a resolved configured type; excludes boxing and user conversions.</summary>
    public bool TypeIsAssignableTo(CodeType type)
    {
        if (!IsResolved || Type is not { } actual)
            return false;
        return type.Resolve(Source.Project.Compilation) is { } target && TypeIsAssignableTo(target)
            || TypeTargets(actual)
                .Any(candidate => type.Matches(candidate) && TypeIsAssignableTo(candidate));
    }

    /// <summary>Tests identity or implicit reference conversion to an exact contextual symbol, including generic variance.</summary>
    public bool TypeIsAssignableTo(ITypeSymbol target)
    {
        if (!IsResolved || Type is not { } actual)
            return false;
        var conversion = Source.Project.Compilation.ClassifyConversion(actual, target);
        return conversion.IsIdentity || conversion.IsImplicit && conversion.IsReference;
    }

    /// <summary>Matches both a constant's compiler type and its value.</summary>
    public bool IsConstant(CodeType type, object? value) =>
        TypeIs(type) && Constant is { HasValue: true } constant && Equals(constant.Value, value);

    /// <summary>Matches the bound operand of nameof; does not establish receiver or object identity.</summary>
    public bool IsNameOf(ISymbol symbol) =>
        Operation is INameOfOperation name
        && ReferencedSymbol(name.Argument) is { } operand
        && SymbolEqualityComparer.Default.Equals(operand, symbol);

    internal static ISymbol? ReferencedSymbol(IOperation operation) =>
        operation switch
        {
            IPropertyReferenceOperation property => property.Property,
            IFieldReferenceOperation field => field.Field,
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            IMethodReferenceOperation method => method.Method,
            IInvocationOperation invocation => invocation.TargetMethod,
            IConversionOperation { IsImplicit: true, Conversion.IsUserDefined: false } conversion =>
                ReferencedSymbol(conversion.Operand),
            IParenthesizedOperation parentheses => ReferencedSymbol(parentheses.Operand),
            _ => null,
        };

    private static IOperation? BoundOperation(AnalysisSource source, ExpressionSyntax syntax)
    {
        while (syntax is ParenthesizedExpressionSyntax parenthesized)
            syntax = parenthesized.Expression;
        return source.Model.GetOperation(syntax, source.Project.CancellationToken);
    }

    private IEnumerable<ITypeSymbol> TypeTargets(ITypeSymbol actual)
    {
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<ITypeSymbol>();
        pending.Push(actual);
        while (pending.TryPop(out var current))
        {
            Source.Project.CancellationToken.ThrowIfCancellationRequested();
            if (!seen.Add(current))
                continue;
            yield return current;
            if (current is ITypeParameterSymbol parameter)
                foreach (var constraint in parameter.ConstraintTypes)
                    pending.Push(constraint);
            if (current is INamedTypeSymbol { IsReferenceType: true } named)
            {
                if (named.BaseType is { } parent)
                    pending.Push(parent);
                foreach (var contract in named.Interfaces)
                    pending.Push(contract);
            }
        }
    }
}
