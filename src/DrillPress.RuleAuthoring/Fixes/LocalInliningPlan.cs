using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

// The edits that inline one local in one compilation membership, after every library-owned gate held there.
internal sealed class LocalInliningPlan
{
    private LocalInliningPlan(
        AnalysisSource source,
        LocalDeclarationStatementSyntax declaration,
        ExpressionSyntax initializer,
        IdentifierNameSyntax read,
        IReadOnlyList<StatementSyntax> skippedStatements,
        IReadOnlyList<IOperation> skippedOperations,
        TextSpan removed,
        string replacement
    )
    {
        Source = source;
        Declaration = declaration;
        Initializer = initializer;
        Read = read;
        SkippedStatements = skippedStatements;
        SkippedOperations = skippedOperations;
        Edits =
        [
            SourceChanges.Replace(source, removed, ""),
            SourceChanges.Replace(source, read.Span, replacement),
        ];
    }

    internal AnalysisSource Source { get; }

    internal LocalDeclarationStatementSyntax Declaration { get; }

    internal ExpressionSyntax Initializer { get; }

    internal IdentifierNameSyntax Read { get; }

    // The statements of the same list between the declaration and the read's statement.
    internal IReadOnlyList<StatementSyntax> SkippedStatements { get; }

    // The outermost operations of the read's statement that run before the read.
    internal IReadOnlyList<IOperation> SkippedOperations { get; }

    // The removal of the declaration's lines, then the replacement of the read.
    internal IReadOnlyList<SourceEdit> Edits { get; }

    // Applies every gate; ordered also requires that the initializer moves past nothing observable.
    internal static LocalInliningPlan? Create(
        AnalysisSource source,
        SyntaxNode? declaration,
        bool ordered,
        out string reason
    )
    {
        if (
            declaration
            is not VariableDeclarationSyntax
            {
                Parent: LocalDeclarationStatementSyntax
                {
                    Parent: BlockSyntax or SwitchSectionSyntax
                } statement,
            } variables
        )
        {
            reason =
                "only a local declaration statement in a block or switch section can be inlined";
            return null;
        }
        if (Shape(statement, variables) is { } shape)
        {
            reason = shape;
            return null;
        }
        if (!source.Document.IsEditable || source.Document.IsGenerated)
        {
            reason = "the declaration is in generated or non-editable source";
            return null;
        }
        var declarator = variables.Variables[0];
        var initializer = declarator.Initializer!.Value;
        if (Removal(source, statement, initializer, out reason) is not { } removed)
            return null;
        if (SingleRead(source, declarator, initializer, out reason) is not { } read)
            return null;
        if (InlineReadPosition.Find(source, statement, read, out reason) is not { } position)
            return null;
        if (UsedInPlace(source, declarator, initializer, position) is { } inPlace)
        {
            reason = inPlace;
            return null;
        }
        var list = statement.Parent is BlockSyntax block
            ? block.Statements
            : ((SwitchSectionSyntax)statement.Parent!).Statements;
        var skipped = list.Skip(list.IndexOf(statement) + 1)
            .TakeWhile(candidate => candidate != position.Statement)
            .ToArray();
        if (Jumps(skipped.Append(position.Statement), skipped) is { } jump)
        {
            reason = jump;
            return null;
        }
        var before = InlineEvaluationOrder.Before(position);
        if (ordered && Unordered(source, statement, initializer, skipped, before) is { } moved)
        {
            reason = $"the initializer would move past {moved}; use SafeWhen";
            return null;
        }
        reason = "";
        return new(
            source,
            statement,
            initializer,
            read,
            skipped,
            before,
            removed,
            ExpressionLayout.FitText(source, read, initializer)
        );
    }

    private static string? Shape(
        LocalDeclarationStatementSyntax statement,
        VariableDeclarationSyntax variables
    ) =>
        variables.Variables.Count != 1 ? "the declaration declares several variables"
        : variables.Variables[0].Initializer is null ? "the local has no initializer"
        : !statement.UsingKeyword.IsKind(SyntaxKind.None) ? "a using declaration disposes its local"
        : statement.IsConst ? "the local is a constant, which names its value"
        : variables.Type is RefTypeSyntax or ScopedTypeSyntax ? "the local is a ref or scoped local"
        : null;

    // The declaration's whole lines, and blank lines that would otherwise follow the block's opening or another
    // blank line. Comments on the declaration would be lost; comments within the initializer move with it.
    private static TextSpan? Removal(
        AnalysisSource source,
        LocalDeclarationStatementSyntax statement,
        ExpressionSyntax initializer,
        out string reason
    )
    {
        reason = "";
        var trivia = statement.DescendantTrivia(descendIntoTrivia: true).ToArray();
        if (
            trivia.Any(item =>
                !initializer.Span.Contains(item.Span)
                && !item.IsKind(SyntaxKind.WhitespaceTrivia)
                && !item.IsKind(SyntaxKind.EndOfLineTrivia)
            )
        )
        {
            reason = "comments or directives on the declaration would be lost";
            return null;
        }
        if (trivia.Any(item => item.IsDirective || item.IsKind(SyntaxKind.DisabledTextTrivia)))
        {
            reason = "the initializer contains preprocessor directives";
            return null;
        }
        var text = source.Tree.GetText(source.Project.CancellationToken);
        var first = text.Lines.GetLineFromPosition(statement.SpanStart);
        var last = text.Lines.GetLineFromPosition(statement.Span.End);
        if (
            !string.IsNullOrWhiteSpace(
                text.ToString(TextSpan.FromBounds(first.Start, statement.SpanStart))
            )
            || !string.IsNullOrWhiteSpace(
                text.ToString(TextSpan.FromBounds(statement.Span.End, last.End))
            )
        )
        {
            reason = "the declaration shares a line with other code";
            return null;
        }
        var end = last.EndIncludingLineBreak;
        var previous = statement.GetFirstToken().GetPreviousToken();
        if (
            previous.Kind() is SyntaxKind.OpenBraceToken or SyntaxKind.ColonToken
            || first.LineNumber > 0 && Blank(text.Lines[first.LineNumber - 1])
        )
            for (
                var next = last.LineNumber + 1;
                next < text.Lines.Count && Blank(text.Lines[next]);
                next++
            )
                end = text.Lines[next].EndIncludingLineBreak;
        return TextSpan.FromBounds(first.Start, end);
    }

    private static bool Blank(TextLine line) =>
        line.EndIncludingLineBreak > line.End && string.IsNullOrWhiteSpace(line.ToString());

    // The local's only reference, when the initializer can stand in for it: it has the local's type, is not
    // target-typed by the declaration and declares no variables, and the local is never written.
    private static IdentifierNameSyntax? SingleRead(
        AnalysisSource source,
        VariableDeclaratorSyntax declarator,
        ExpressionSyntax initializer,
        out string reason
    )
    {
        var token = source.Project.CancellationToken;
        reason = "";
        if (initializer.DescendantNodesAndSelf().OfType<SingleVariableDesignationSyntax>().Any())
            reason = "the initializer declares variables whose scope would change";
        else if (VarRewrite.HasTargetTyping(source, initializer))
            reason = "the initializer is target-typed by the declaration";
        else if (source.Model.GetDeclaredSymbol(declarator, token) is not ILocalSymbol local)
            reason = "the declaration has no symbol";
        else if (
            source.Model.GetTypeInfo(initializer, token).Type
            is not { TypeKind: not TypeKind.Error } type
        )
            reason = "the initializer has no type";
        else if (!SymbolEqualityComparer.Default.Equals(local.Type, type))
            reason = "the local's type differs from its initializer's type";
        else if (source.LocalAssignments.Initializer(local) is null)
            reason = "the local is written after its declaration or passed by reference";
        else if (LocalReferences.Find(source, local, declarator) is not [var read])
            reason = "the local is not read exactly once";
        else if (ContextualRewrite.IsObservableSyntax(source, read))
            reason = "the read is inside nameof or an expression tree";
        else
            return read;
        return null;
    }

    // A struct method or property reached through the local acts on a copy; reached through the initializer's
    // variable, it would act on that variable.
    private static string? UsedInPlace(
        AnalysisSource source,
        VariableDeclaratorSyntax declarator,
        ExpressionSyntax initializer,
        InlineReadPosition position
    )
    {
        var type = ((ILocalSymbol)source.Model.GetDeclaredSymbol(declarator)!).Type;
        if (type.IsReferenceType || type is { IsValueType: true, IsReadOnly: true })
            return null;
        var parent = position.Read.Parent;
        var receiver =
            parent is IInvocationOperation call && call.Instance == position.Read
            || parent is IMemberReferenceOperation member && member.Instance == position.Read;
        return
            receiver
            && IsVariable(source.Model.GetOperation(initializer, source.Project.CancellationToken))
            ? $"{FixReasons.Describe(source, parent!.Syntax)} would use the initializer's variable instead of a copy"
            : null;
    }

    private static bool IsVariable(IOperation? operation) =>
        operation switch
        {
            null => true,
            IParenthesizedOperation parenthesized => IsVariable(parenthesized.Operand),
            IConversionOperation { Conversion.IsIdentity: true } conversion => IsVariable(
                conversion.Operand
            ),
            IInvocationOperation call => call.TargetMethod.ReturnsByRef
                || call.TargetMethod.ReturnsByRefReadonly,
            IPropertyReferenceOperation property => property.Property.ReturnsByRef
                || property.Property.ReturnsByRefReadonly,
            IConditionalOperation conditional => conditional.IsRef,
            ILocalReferenceOperation
            or IParameterReferenceOperation
            or IFieldReferenceOperation
            or IArrayElementReferenceOperation
            or IInstanceReferenceOperation
            or IInlineArrayAccessOperation
            or IImplicitIndexerReferenceOperation
            or IFunctionPointerInvocationOperation
            or IInvalidOperation => true,
            _ => false,
        };

    // A label up to the read lets a goto run the initializer again, and a jump in a skipped statement may
    // leave before it runs at all; either breaks evaluating it exactly once.
    private static string? Jumps(
        IEnumerable<StatementSyntax> statements,
        IReadOnlyList<StatementSyntax> skipped
    )
    {
        if (statements.Any(statement => statement is LabeledStatementSyntax))
            return "a label between the declaration and the read could run the initializer again";
        return skipped.Any(statement =>
            statement
                .DescendantNodesAndSelf(node =>
                    node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
                )
                .Any(node => LeavesBlock(node, statement))
        )
            ? "a statement before the read may jump past it"
            : null;
    }

    private static bool LeavesBlock(SyntaxNode node, StatementSyntax skipped) =>
        node
            is ReturnStatementSyntax
                or ThrowStatementSyntax
                or ThrowExpressionSyntax
                or GotoStatementSyntax
                or YieldStatementSyntax { RawKind: (int)SyntaxKind.YieldBreakStatement }
        || node is BreakStatementSyntax or ContinueStatementSyntax
            && !node.Ancestors()
                .TakeWhile(ancestor => ancestor != skipped.Parent)
                .Any(ancestor =>
                    ancestor
                        is ForStatementSyntax
                            or CommonForEachStatementSyntax
                            or WhileStatementSyntax
                            or DoStatementSyntax
                    || node is BreakStatementSyntax && ancestor is SwitchStatementSyntax
                );

    // The first skipped statement or impure operand, described for the withheld reason.
    private static string? Unordered(
        AnalysisSource source,
        LocalDeclarationStatementSyntax statement,
        ExpressionSyntax initializer,
        IReadOnlyList<StatementSyntax> skipped,
        IReadOnlyList<IOperation> before
    ) =>
        skipped.Count > 0 ? FixReasons.Describe(source, skipped[0])
        : InlineEvaluationOrder.FirstImpure(source, statement, initializer, before) is { } impure
            ? FixReasons.Describe(source, impure.Syntax)
        : null;
}
