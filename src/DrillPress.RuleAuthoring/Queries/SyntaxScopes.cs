using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>Explicit syntax boundaries and source facts for rules that inspect written structure rather than execution.</summary>
public static class SyntaxScopes
{
    /// <summary>Returns the non-nullable original declaration display name, or an empty string when unknown.</summary>
    public static string TypeName(ITypeSymbol? type) =>
        type is null
            ? ""
            : (
                type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).OriginalDefinition
            ).ToDisplayString();

    /// <summary>Tests a nullable-unwrapped type against its original declaration display name, ignoring constructed arguments.</summary>
    public static bool IsType(ITypeSymbol? type, string name) =>
        type is not null
        && TypeName(
            type
                is INamedTypeSymbol
                {
                    IsGenericType: true,
                    ConstructedFrom.SpecialType: SpecialType.System_Nullable_T
                } nullable
                ? nullable.TypeArguments[0]
                : type
        ) == name;

    /// <summary>The type whose code contains <paramref name="node"/>, including code in lambdas and local functions.</summary>
    public static INamedTypeSymbol? EnclosingType(AnalysisSource source, SyntaxNode node) =>
        source.Model.GetEnclosingSymbol(node.SpanStart) switch
        {
            INamedTypeSymbol type => type,
            { } symbol => symbol.ContainingType,
            null => null,
        };

    /// <summary>Tests the enclosing type and its containing types against a display name.</summary>
    public static bool IsInType(AnalysisSource source, SyntaxNode node, string typeName)
    {
        for (var type = EnclosingType(source, node); type is not null; type = type.ContainingType)
        {
            if (type.ToDisplayString() == typeName)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Tests the enclosing type and its containing types against a semantic type descriptor.</summary>
    public static bool IsInType(AnalysisSource source, SyntaxNode node, CodeType target)
    {
        for (var type = EnclosingType(source, node); type is not null; type = type.ContainingType)
            if (target.Matches(type))
                return true;
        return false;
    }

    /// <summary>The enclosing method symbol; optionally walks out of lambdas and local functions to the member that declares them.</summary>
    public static IMethodSymbol? EnclosingMethod(
        AnalysisSource source,
        SyntaxNode node,
        bool skipNested = true
    )
    {
        var symbol = source.Model.GetEnclosingSymbol(
            node.SpanStart,
            source.Project.CancellationToken
        );
        while (
            skipNested
            && symbol
                is IMethodSymbol
                {
                    MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction
                }
        )
            symbol = symbol.ContainingSymbol;
        return symbol as IMethodSymbol;
    }

    /// <summary>Whether syntax declares an anonymous function or local function, whose body belongs to a separate execution scope.</summary>
    public static bool IsNestedFunction(SyntaxNode node) =>
        node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax;

    /// <summary>The names of the methods called on the result of <paramref name="call"/> in a fluent chain, such as <c>.Bind(...).ValidateOnStart()</c>.</summary>
    public static List<string> ChainAfter(InvocationExpressionSyntax call, out ExpressionSyntax top)
    {
        var names = new List<string>();
        ExpressionSyntax current = call;
        while (
            current.Parent is MemberAccessExpressionSyntax access
            && access.Expression == current
            && access.Parent is InvocationExpressionSyntax next
        )
        {
            names.Add(access.Name.Identifier.ValueText);
            current = next;
        }
        top = current;
        return names;
    }

    /// <summary>The member declaration that contains <paramref name="node"/>, outside any lambda.</summary>
    public static SyntaxNode? ContainingMember(SyntaxNode node) =>
        node.Ancestors()
            .FirstOrDefault(ancestor =>
                ancestor
                    is BaseMethodDeclarationSyntax
                        or PropertyDeclarationSyntax
                        or FieldDeclarationSyntax
                        or GlobalStatementSyntax
            );

    /// <summary>The nodes of <paramref name="root"/>, without those inside lambdas and local functions, which run at another time.</summary>
    public static IEnumerable<SyntaxNode> OwnNodes(SyntaxNode root) =>
        OwnNodes(root, includeSelf: false);

    /// <summary>The nodes of the root without entering nested functions; optionally includes the root itself.</summary>
    public static IEnumerable<SyntaxNode> OwnNodes(SyntaxNode root, bool includeSelf) =>
        includeSelf
            ? root.DescendantNodesAndSelf(node => node == root || !IsNestedFunction(node))
            : root.DescendantNodes(node => node == root || !IsNestedFunction(node));

    /// <summary>Whether an unfiltered catch is untyped or catches System.Exception.</summary>
    public static bool IsCatchAll(CatchClauseSyntax clause, SemanticModel model) =>
        clause.Filter is null
        && (
            clause.Declaration is null
            || TypeName(model.GetTypeInfo(clause.Declaration.Type).Type) == "System.Exception"
        );

    /// <summary>Tests a type and its base classes against an original declaration display name.</summary>
    public static bool TypeDerivesFrom(ITypeSymbol type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (TypeName(current) == name)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The value of a string constant, or null when <paramref name="expression"/> isn't one.</summary>
    public static string? ConstantText(SemanticModel model, ExpressionSyntax expression) =>
        model.GetConstantValue(expression) is { HasValue: true, Value: string text } ? text : null;

    /// <summary>Whether <paramref name="expression"/> builds a string at run time by interpolation, concatenation or formatting.</summary>
    public static bool IsBuiltString(SemanticModel model, ExpressionSyntax expression)
    {
        if (ConstantText(model, expression) is not null)
        {
            return false;
        }
        return expression switch
        {
            InterpolatedStringExpressionSyntax => true,
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression) => true,
            ParenthesizedExpressionSyntax parenthesized => IsBuiltString(
                model,
                parenthesized.Expression
            ),
            InvocationExpressionSyntax invocation => model.GetSymbolInfo(invocation).Symbol
                is IMethodSymbol method
                && TypeName(method.ContainingType) == "System.String"
                && method.Name is "Format" or "Concat" or "Join",
            _ => false,
        };
    }

    /// <summary>The nearest enclosing loop without crossing a function or member boundary.</summary>
    public static StatementSyntax? EnclosingLoop(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (
                ancestor
                is AnonymousFunctionExpressionSyntax
                    or LocalFunctionStatementSyntax
                    or MemberDeclarationSyntax
            )
            {
                return null;
            }
            if (ancestor is StatementSyntax statement && IsLoop(statement))
            {
                return statement;
            }
        }
        return null;
    }

    /// <summary>Whether syntax is a for, foreach, while or do loop.</summary>
    public static bool IsLoop(SyntaxNode node) =>
        node
            is WhileStatementSyntax
                or DoStatementSyntax
                or ForStatementSyntax
                or CommonForEachStatementSyntax;

    /// <summary>The written condition of a for, while or do loop, otherwise null.</summary>
    public static SyntaxNode? LoopCondition(SyntaxNode loop) =>
        loop switch
        {
            WhileStatementSyntax statement => statement.Condition,
            DoStatementSyntax statement => statement.Condition,
            ForStatementSyntax statement => statement.Condition,
            _ => null,
        };

    // Blank lines in the test's own body, leaving out those in multi-line strings and in doubles declared in the test.
    /// <summary>Counts whitespace-only lines inside a block, excluding lines inside tokens and nested functions.</summary>
    public static int BlankLines(CodeBody scope)
    {
        if (scope.Root is not BlockSyntax body)
        {
            return 0;
        }
        var text = scope.Source.Tree.GetText();
        var first = text.Lines.GetLineFromPosition(body.OpenBraceToken.Span.End).LineNumber + 1;
        var last = text.Lines.GetLineFromPosition(body.CloseBraceToken.SpanStart).LineNumber - 1;
        var functions = body.DescendantNodes()
            .Where(node =>
                node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax
            )
            .Select(function => function.Span)
            .ToList();
        var count = 0;
        for (var line = first; line <= last; line++)
        {
            var span = text.Lines[line].Span;
            if (
                string.IsNullOrWhiteSpace(text.ToString(span))
                && !InsideToken(body, span.Start)
                && !functions.Any(function => function.Contains(span.Start))
            )
            {
                count++;
            }
        }
        return count;
    }

    private static bool InsideToken(SyntaxNode body, int position)
    {
        var token = body.FindToken(position);
        return token.Span.Start < position && position < token.Span.End;
    }

    /// <summary>Whether syntax is nested in a lambda converted to System.Linq.Expressions.Expression.</summary>
    public static bool IsInsideExpressionTree(SemanticModel model, SyntaxNode node) =>
        node.Ancestors()
            .OfType<LambdaExpressionSyntax>()
            .Any(lambda =>
                model.GetTypeInfo(lambda).ConvertedType is INamedTypeSymbol type
                && type.OriginalDefinition.ToDisplayString()
                    .StartsWith("System.Linq.Expressions.Expression<", StringComparison.Ordinal)
            );
}
