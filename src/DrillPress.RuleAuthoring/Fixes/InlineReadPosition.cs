using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

// Where a local's single read sits: the header of a later statement in the declaration's statement list,
// evaluated exactly once and unconditionally when that header runs.
internal sealed class InlineReadPosition
{
    private InlineReadPosition(StatementSyntax statement, IOperation header, IOperation read)
    {
        Statement = statement;
        Header = header;
        Read = read;
    }

    // The statement whose header reads the local.
    internal StatementSyntax Statement { get; }

    // The operation of the evaluated header: the whole statement, or its condition or resource expression.
    internal IOperation Header { get; }

    // The local reference.
    internal IOperation Read { get; }

    internal static InlineReadPosition? Find(
        AnalysisSource source,
        LocalDeclarationStatementSyntax declaration,
        IdentifierNameSyntax read,
        out string reason
    )
    {
        reason = "";
        var statement = read.Ancestors()
            .OfType<StatementSyntax>()
            .FirstOrDefault(candidate => candidate.Parent == declaration.Parent);
        var region = statement is null ? null : HeaderRegion(statement);
        if (
            statement is null
            || statement.SpanStart < declaration.Span.End
            || region is null
            || !region.Span.Contains(read.Span)
        )
        {
            reason = "the read is not evaluated once by a later statement of the same block";
            return null;
        }
        if (Deferral(read, region) is { } deferred)
        {
            reason = $"the read is deferred inside {FixReasons.Describe(source, deferred)}";
            return null;
        }
        if (
            read.Ancestors()
                .TakeWhile(node => node != region)
                .OfType<CheckedExpressionSyntax>()
                .Any()
        )
        {
            reason =
                "the read is inside checked or unchecked, which would change the initializer's overflow checking";
            return null;
        }
        var token = source.Project.CancellationToken;
        var header =
            region is StatementSyntax
                ? source.Model.GetOperation(region, token)
                : Outermost(source.Model.GetOperation(region, token));
        // Taken from the header's own tree, so that parents and children are the same instances.
        var operation = header
            ?.DescendantsAndSelf()
            .FirstOrDefault(candidate =>
                candidate is ILocalReferenceOperation && candidate.Syntax == read
            );
        if (header is null || operation is null)
        {
            reason = "the read has no bound operation";
            return null;
        }
        if (Unconditional(source, operation, region) is { } conditional)
        {
            reason = conditional;
            return null;
        }
        return new(statement, header, operation);
    }

    // The part of a statement that runs once before any nested statement or loop iteration.
    private static SyntaxNode? HeaderRegion(StatementSyntax statement) =>
        statement switch
        {
            ExpressionStatementSyntax
            or ReturnStatementSyntax
            or ThrowStatementSyntax
            or LocalDeclarationStatementSyntax => statement,
            YieldStatementSyntax yield when yield.IsKind(SyntaxKind.YieldReturnStatement) =>
                statement,
            IfStatementSyntax branch => branch.Condition,
            SwitchStatementSyntax selection => selection.Expression,
            CommonForEachStatementSyntax loop => loop.Expression,
            UsingStatementSyntax resource => (SyntaxNode?)resource.Expression
                ?? resource.Declaration,
            LockStatementSyntax guarded => guarded.Expression,
            _ => null,
        };

    private static SyntaxNode? Deferral(SyntaxNode read, SyntaxNode region) =>
        read.Ancestors()
            .TakeWhile(node => node != region)
            .FirstOrDefault(node =>
                node
                    is AnonymousFunctionExpressionSyntax
                        or LocalFunctionStatementSyntax
                        or QueryExpressionSyntax
            );

    // The operation for a header expression, including implicit conversions sharing its syntax.
    private static IOperation? Outermost(IOperation? operation)
    {
        while (operation?.Parent is { } parent && parent.Syntax == operation.Syntax)
            operation = parent;
        return operation;
    }

    // Walks from the read to the header, accepting only parents that evaluate the child exactly once whenever
    // they are evaluated themselves.
    private static string? Unconditional(AnalysisSource source, IOperation read, SyntaxNode region)
    {
        var child = read;
        while (child.Parent is { } parent && region.Span.Contains(parent.Syntax.Span))
        {
            if (Conditional(child, parent) is { } kind)
                return kind switch
                {
                    Evaluation.Conditional =>
                        $"the read may not be evaluated in {FixReasons.Describe(source, parent.Syntax)}",
                    Evaluation.CompiledAway =>
                        $"the read is part of {FixReasons.Describe(source, parent.Syntax)}, which a [Conditional] attribute or a missing partial implementation may compile away",
                    Evaluation.Handler =>
                        $"the read is in an interpolated string handler at {FixReasons.Position(source, parent.Syntax.SpanStart)}, which may skip it",
                    _ =>
                        $"the read's position in {FixReasons.Describe(source, parent.Syntax)} is not supported",
                };
            child = parent;
        }
        return null;
    }

    private static Evaluation? Conditional(IOperation child, IOperation parent) =>
        parent switch
        {
            IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr,
            } binary => binary.LeftOperand == child ? null : Evaluation.Conditional,
            ICoalesceOperation coalesce => coalesce.Value == child ? null : Evaluation.Conditional,
            ICoalesceAssignmentOperation assignment => assignment.Target == child
                ? null
                : Evaluation.Conditional,
            IConditionalOperation conditional => conditional.Condition == child
                ? null
                : Evaluation.Conditional,
            IConditionalAccessOperation access => access.Operation == child
                ? null
                : Evaluation.Conditional,
            ISwitchExpressionOperation selection => selection.Value == child
                ? null
                : Evaluation.Conditional,
            IIsPatternOperation pattern => pattern.Value == child ? null : Evaluation.Conditional,
            IInvocationOperation call => CompiledAway(call.TargetMethod)
                ? Evaluation.CompiledAway
                : null,
            IInterpolatedStringAppendOperation or IInterpolatedStringHandlerCreationOperation =>
                Evaluation.Handler,
            IConversionOperation
            or IParenthesizedOperation
            or IArgumentOperation
            or IBinaryOperation
            or IUnaryOperation
            or IObjectCreationOperation
            or IAnonymousObjectCreationOperation
            or IObjectOrCollectionInitializerOperation
            or IMemberInitializerOperation
            or IPropertyReferenceOperation
            or IFieldReferenceOperation
            or IEventReferenceOperation
            or IMethodReferenceOperation
            or IArrayElementReferenceOperation
            or IArrayCreationOperation
            or IArrayInitializerOperation
            or ICollectionExpressionOperation
            or ISpreadOperation
            or ITupleOperation
            or IInterpolatedStringOperation
            or IInterpolationOperation
            or IAwaitOperation
            or IWithOperation
            or IRangeOperation
            or IIsTypeOperation
            or IDelegateCreationOperation
            or ISimpleAssignmentOperation
            or ICompoundAssignmentOperation
            or IDeconstructionAssignmentOperation
            or IEventAssignmentOperation
            or IReturnOperation
            or IThrowOperation
            or IExpressionStatementOperation
            or IVariableInitializerOperation
            or IVariableDeclaratorOperation
            or IVariableDeclarationOperation
            or IVariableDeclarationGroupOperation
            or IUsingDeclarationOperation => null,
            _ => Evaluation.Unsupported,
        };

    // A call to a [Conditional] method, or to a partial method without an implementation, is removed with its
    // receiver and argument evaluation in some or all compilations.
    private static bool CompiledAway(IMethodSymbol method)
    {
        if (method.IsPartialDefinition && method.PartialImplementationPart is null)
            return true;
        // Overrides of a [Conditional] method are conditional too.
        for (
            IMethodSymbol? candidate = method;
            candidate is not null;
            candidate = candidate.OverriddenMethod
        )
            if (
                candidate
                    .GetAttributes()
                    .Any(attribute =>
                        attribute.AttributeClass?.ToDisplayString()
                        == "System.Diagnostics.ConditionalAttribute"
                    )
            )
                return true;
        return false;
    }

    private enum Evaluation
    {
        Conditional,
        CompiledAway,
        Handler,
        Unsupported,
    }
}
