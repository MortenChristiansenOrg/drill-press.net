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

    /// <summary>Proposes the assignment when the original creation keeps its type and the new value binds to the selected member. Existing assignments are never overwritten. The caller proves the new value's meaning.</summary>
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
        return SourceChanges.Propose(
            [edit],
            context =>
            {
                if (
                    context.Map(_source, creation)
                        is not {
                            After: BaseObjectCreationExpressionSyntax { Initializer: { } after }
                        } mapped
                    || !context.Same(before, mapped.Model.GetTypeInfo(mapped.After).Type)
                    || context.RewrittenSource(_source) is not { } rewritten
                )
                    return false;
                var assigned = after
                    .Expressions.OfType<AssignmentExpressionSyntax>()
                    .FirstOrDefault(assignment =>
                        assignment.Left is IdentifierNameSyntax name
                        && name.Identifier.ValueText == _member
                    );
                return assigned is not null
                    && mapped.Model.GetOperation(assigned) is ISimpleAssignmentOperation assignment
                    && assignment.Target is IMemberReferenceOperation member
                    && member.Member.Name == _member
                    && proof(new CodeExpression(rewritten, assigned.Right));
            }
        );
    }
}
