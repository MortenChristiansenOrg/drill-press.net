using DrillPress.Operations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress.Queries;

/// <summary>Configurable static type discovery in method declarations and executable bodies.</summary>
public sealed class TypeSeedSelector(Func<CodeMethod, IEnumerable<TypeSeed>> select)
{
    /// <summary>Evaluates this selector for one method; custom selectors must be pure, finite and cancellation-aware.</summary>
    public IEnumerable<TypeSeed> In(CodeMethod method) => select(method);

    /// <summary>Reads a constructed attribute type argument. The predicate can select status/other attribute policy; no attributes are inherited implicitly.</summary>
    public static TypeSeedSelector AttributeTypeArgument(
        CodeType attribute,
        int index,
        Func<CodeAttribute, bool>? where = null
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return Attributes(
            attribute,
            data =>
                data.Data.AttributeClass is { } type && index < type.TypeArguments.Length
                    ? type.TypeArguments[index]
                    : null,
            where
        );
    }

    /// <summary>Reads a typeof constructor argument by bound parameter name; absent or erroneous values retain an unresolved seed.</summary>
    public static TypeSeedSelector AttributeConstructorType(
        CodeType attribute,
        string parameter,
        Func<CodeAttribute, bool>? where = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
        return Attributes(attribute, data => TypeOf(data.ConstructorArgument(parameter)), where);
    }

    /// <summary>Reads an explicitly supplied typeof named attribute value; it does not execute a property initializer.</summary>
    public static TypeSeedSelector AttributeNamedType(
        CodeType attribute,
        string name,
        Func<CodeAttribute, bool>? where = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Attributes(attribute, data => TypeOf(data.NamedArgument(name)), where);
    }

    /// <summary>Reads the original expression types of values mapped to an exact configured call parameter, excluding nested functions by default.</summary>
    public static TypeSeedSelector InvocationArgument(
        CodeMember member,
        string parameter,
        Func<CodeInvocation, bool>? where = null,
        NestedFunctions nested = NestedFunctions.Exclude
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
        return new(method =>
            Body(method, nested)
                ?.Invocations()
                .Where(call => call.Calls(member) && (where?.Invoke(call) ?? true))
                .SelectMany(call =>
                    call.Parameter(parameter) is { } mapped
                        ? mapped.Values.Select(value => new TypeSeed(
                            value.Value ?? (ICodeElement)call,
                            value.Value?.Type,
                            value
                        ))
                        : [new TypeSeed(call, null, call)]
                )
            ?? []
        );
    }

    /// <summary>Reads original argument expression types from exact configured constructors, including target-typed new; no runtime types are inferred.</summary>
    public static TypeSeedSelector ConstructorArgument(
        CodeMember constructor,
        string parameter,
        Func<IObjectCreationOperation, bool>? where = null,
        NestedFunctions nested = NestedFunctions.Exclude
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
        return new(method =>
            (Body(method, nested)?.Nodes<BaseObjectCreationExpressionSyntax>() ?? []).SelectMany(
                node =>
                    node.Operation is IObjectCreationOperation { Constructor: { } target } creation
                    && constructor.Matches(target)
                    && (where?.Invoke(creation) ?? true)
                        ? ConstructorSeeds(node, creation, parameter)
                        : []
            )
        );
    }

    private static IEnumerable<TypeSeed> ConstructorSeeds(
        CodeNode<BaseObjectCreationExpressionSyntax> node,
        IObjectCreationOperation creation,
        string parameter
    )
    {
        var arguments = creation
            .Arguments.Where(argument => argument.Parameter?.Name == parameter)
            .ToArray();
        if (arguments.Length == 0)
            return [new(node, null, creation)];
        return arguments.Select(argument =>
            argument.Syntax is ArgumentSyntax syntax
                ? new TypeSeed(
                    new CodeExpression(node.Source, syntax.Expression),
                    node.Source.Model.GetTypeInfo(syntax.Expression).Type,
                    argument
                )
                : new TypeSeed(node, null, argument)
        );
    }

    private static TypeSeedSelector Attributes(
        CodeType attribute,
        Func<CodeAttribute, ITypeSymbol?> type,
        Func<CodeAttribute, bool>? where
    ) =>
        new(method =>
            method
                .Symbol?.Attributes()
                .Where(data =>
                    data.Matches(attribute, includeDerived: false) && (where?.Invoke(data) ?? true)
                )
                .Select(data => new TypeSeed(AttributeAnchor(method, data), type(data), data))
            ?? []
        );

    private static ICodeElement AttributeAnchor(CodeMethod method, CodeAttribute attribute) =>
        attribute.Data.ApplicationSyntaxReference?.GetSyntax(
            method.Source.Project.CancellationToken
        )
            is { } syntax
        && syntax.SyntaxTree == method.Source.Tree
            ? new CodeNode<SyntaxNode>(method.Source, syntax)
            : method;

    private static ITypeSymbol? TypeOf(Optional<TypedConstant> value) =>
        value
            is { HasValue: true, Value.Kind: TypedConstantKind.Type, Value.Value: ITypeSymbol type }
            ? type
            : null;

    private static CodeBody? Body(CodeMethod method, NestedFunctions nested) =>
        ((SyntaxNode?)method.Syntax.Body ?? method.Syntax.ExpressionBody?.Expression) is { } body
            ? new(method.Source, body, nested)
            : null;
}
