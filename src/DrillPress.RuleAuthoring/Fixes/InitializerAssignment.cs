using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

/// <summary>Adds one previously unassigned simple member to an object initializer.</summary>
public sealed class InitializerAssignment
{
    private readonly AnalysisSource _source;
    private readonly ExpressionSyntax _syntax;
    private readonly string _member;
    private readonly string _value;

    internal InitializerAssignment(
        AnalysisSource source,
        ExpressionSyntax syntax,
        string member,
        string value
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(member);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (
            !SyntaxFacts.IsValidIdentifier(member)
            || SyntaxFactory.ParseExpression(value).ContainsDiagnostics
        )
            throw new ArgumentException(
                "Supply a simple member identifier and a complete value expression."
            );
        _source = source;
        _syntax = syntax;
        _member = member;
        _value = value;
    }

    /// <summary>Proposes the assignment when the original creation keeps its type, enclosing expressions keep their bindings, compiler-supplied arguments such as caller argument expressions are unchanged, and the new value binds to the selected member. Existing assignments are never overwritten. The caller proves the new value's meaning.</summary>
    public FixProposal? SafeWhen(Func<CodeExpression, bool> proof)
    {
        if (_syntax is not BaseObjectCreationExpressionSyntax creation)
            return null;
        var initializer = creation.Initializer;
        if (
            initializer?.Expressions.Any(expression =>
                expression is AssignmentExpressionSyntax { Left: IdentifierNameSyntax name }
                && name.Identifier.ValueText == _member
            ) == true
        )
            return null;
        var text = _member + " = " + _value;
        var edit = initializer is null
            ? SourceChanges.Replace(
                _source,
                creation.Span,
                creation.ToString() + " { " + text + " }"
            )
            : SourceChanges.Replace(
                _source,
                new(initializer.OpenBraceToken.Span.End, 0),
                initializer.Expressions.Count == 0 ? " " + text + " " : " " + text + ","
            );
        var before = _source.Model.GetTypeInfo(creation).Type;
        return SourceChanges.ProposeExplained(
            [edit],
            context =>
            {
                if (
                    context.Evidence(_source, creation, [edit]) is not { } evidence
                    || context.Map(_source, creation)
                        is not {
                            After: BaseObjectCreationExpressionSyntax { Initializer: { } after }
                        } mapped
                    || !context.Same(before, mapped.Model.GetTypeInfo(mapped.After).Type)
                    || context.RewrittenSource(_source) is not { } rewritten
                )
                    return "the creation does not keep its type after the edit";
                if (RewriteChecks.EnclosingBindings(evidence) is { Holds: false } enclosing)
                    return enclosing.Reason;
                if (
                    RewriteChecks.CompilerSuppliedArguments(evidence, null) is
                    { Holds: false } supplied
                )
                    return supplied.Reason;
                var assigned = after
                    .Expressions.OfType<AssignmentExpressionSyntax>()
                    .FirstOrDefault(assignment =>
                        assignment.Left is IdentifierNameSyntax name
                        && name.Identifier.ValueText == _member
                    );
                if (
                    assigned is null
                    || mapped.Model.GetOperation(assigned)
                        is not ISimpleAssignmentOperation
                        {
                            Target: IMemberReferenceOperation member
                        }
                    || member.Member.Name != _member
                )
                    return "the new assignment does not bind to the member";
                return proof(new CodeExpression(rewritten, assigned.Right))
                    ? null
                    : "the rule's SafeWhen proof returned false";
            }
        );
    }
}
