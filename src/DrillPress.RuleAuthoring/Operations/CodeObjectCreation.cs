using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

/// <summary>A written object creation such as <c>new HttpClient()</c> or target-typed <c>new()</c>, with its bound type and constructor.</summary>
public sealed class CodeObjectCreation : ICodeElement
{
    private readonly Lazy<IObjectCreationOperation?> _operation;

    internal CodeObjectCreation(AnalysisSource source, BaseObjectCreationExpressionSyntax syntax)
    {
        Source = source;
        Syntax = syntax;
        _operation = new(() =>
            source.Model.GetOperation(syntax, source.Project.CancellationToken)
            as IObjectCreationOperation
        );
    }

    /// <summary>The original document and compilation membership.</summary>
    public AnalysisSource Source { get; }

    /// <summary>The written creation, including arguments and any initializer.</summary>
    public BaseObjectCreationExpressionSyntax Syntax { get; }

    /// <summary>The bound creation; null for type-parameter, delegate or invalid creations.</summary>
    public IObjectCreationOperation? Operation => _operation.Value;

    /// <summary>Whether the creation binds to a constructor without compiler errors.</summary>
    public bool IsResolved => Expression.IsResolved && Constructor is not null;

    /// <summary>Whether the type is inferred from the target, as in <c>Widget w = new();</c>.</summary>
    public bool IsTargetTyped => Syntax is ImplicitObjectCreationExpressionSyntax;

    /// <summary>The created type, including constructed generic arguments.</summary>
    public ITypeSymbol? Type => Operation?.Type;

    /// <summary>The selected constructor overload.</summary>
    public IMethodSymbol? Constructor => Operation?.Constructor;

    /// <summary>Matches the created type exactly; open generic descriptors match every construction.</summary>
    public bool Creates(CodeType type) => IsResolved && Type is { } actual && type.Matches(actual);

    /// <summary>Matches the created type, including constructed generic arguments.</summary>
    public bool Creates<T>() => Creates(CodeType.Of<T>());

    /// <summary>Matches the selected constructor, for example <c>CodeType.Of&lt;T&gt;().Constructor(...)</c>.</summary>
    public bool Calls(CodeMember constructor) =>
        Constructor is { } selected && constructor.Matches(selected);

    /// <summary>The creation as an expression with type, constant and context helpers.</summary>
    public CodeExpression Expression => new(Source, Syntax);

    /// <summary>The written argument values in source order, excluding any object or collection initializer.</summary>
    public IReadOnlyList<CodeExpression> Arguments =>
        Array.AsReadOnly(
            (Syntax.ArgumentList?.Arguments ?? [])
                .Select(argument => new CodeExpression(Source, argument.Expression))
                .ToArray()
        );

    /// <summary>The written value bound to a constructor parameter, including named and reordered arguments; null for omitted defaults and expanded params.</summary>
    public CodeExpression? Argument(string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        return
            Operation
                ?.Arguments.Where(argument => argument.Parameter?.Name == parameterName)
                .ToArray()
                is [{ ArgumentKind: ArgumentKind.Explicit, Syntax: ArgumentSyntax syntax }]
            ? new(Source, syntax.Expression)
            : null;
    }

    /// <summary>The complete creation expression.</summary>
    public SourceLocation Location => Source.Locate(Syntax.Span);
}
