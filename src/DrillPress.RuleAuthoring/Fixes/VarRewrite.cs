using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

internal static class VarRewrite
{
    private static readonly SymbolDisplayFormat _typeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );

    internal static bool CanUseVar(CodeVariableDeclaration declaration)
    {
        declaration.Source.Project.CancellationToken.ThrowIfCancellationRequested();
        if (!Eligible(declaration))
            return false;
        var source = declaration.Source;
        var edit = SourceChanges.Replace(source, declaration.TypeSyntax.Span, "var");
        var tree = source.Tree.WithChangedText(
            source
                .Tree.GetText(source.Project.CancellationToken)
                .WithChanges(new TextChange(declaration.TypeSyntax.Span, "var"))
        );
        var compilation = source.Project.Compilation.ReplaceSyntaxTree(source.Tree, tree);
        if (compilation.Options.SyntaxTreeOptionsProvider is { } provider)
            compilation = compilation.WithOptions(
                compilation.Options.WithSyntaxTreeOptionsProvider(
                    new RewrittenTreeOptions(provider, source.Tree, tree)
                )
            );
        var context = new RewriteContext(source.Project, compilation, [edit]);
        return Validate(context, source, declaration);
    }

    internal static FixProposal Propose(CodeVariableDeclaration declaration) =>
        SourceChanges.Propose(
            [SourceChanges.Replace(declaration.Source, declaration.TypeSyntax.Span, "var")],
            context =>
                context
                    .Original.Sources.Where(source =>
                        source.Document.FileIdentity == declaration.Source.Document.FileIdentity
                    )
                    .All(source => Validate(context, source, declaration))
        );

    private static bool Eligible(CodeVariableDeclaration declaration)
    {
        if (
            !declaration.HasExplicitType
            || declaration.TypeSyntax is RefTypeSyntax
            || ContextualRewrite.HasInteriorContent(declaration.TypeSyntax)
        )
            return false;
        return declaration.Syntax switch
        {
            VariableDeclarationSyntax { Variables.Count: 1 } local => local
                .Variables[0]
                .Initializer
                ?.Value
                is { } value
                && !HasTargetTyping(declaration.Source, value)
                && SameUnderlyingType(
                    declaration.Source.Model.GetDeclaredSymbol(
                        local.Variables[0],
                        declaration.Source.Project.CancellationToken
                    )
                        is ILocalSymbol symbol
                        ? symbol.Type
                        : null,
                    declaration
                        .Source.Model.GetTypeInfo(
                            value,
                            declaration.Source.Project.CancellationToken
                        )
                        .Type
                ),
            ForEachStatementSyntax loop => declaration.Source.Model.GetDeclaredSymbol(
                loop,
                declaration.Source.Project.CancellationToken
            )
                is ILocalSymbol symbol
                && declaration.Source.Model.GetForEachStatementInfo(loop) is var info
                && info.ElementConversion.IsIdentity
                && SameType(symbol.Type, info.ElementType),
            DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax } => true,
            _ => false,
        };
    }

    private static bool HasTargetTyping(AnalysisSource source, ExpressionSyntax expression) =>
        expression switch
        {
            ImplicitObjectCreationExpressionSyntax
            or CollectionExpressionSyntax
            or AnonymousFunctionExpressionSyntax => true,
            LiteralExpressionSyntax literal
                when literal.IsKind(SyntaxKind.DefaultLiteralExpression)
                    || literal.IsKind(SyntaxKind.NullLiteralExpression) => true,
            ParenthesizedExpressionSyntax parenthesized => HasTargetTyping(
                source,
                parenthesized.Expression
            ),
            ConditionalExpressionSyntax conditional => HasTargetTyping(source, conditional.WhenTrue)
                || HasTargetTyping(source, conditional.WhenFalse),
            SwitchExpressionSyntax selection => selection.Arms.Any(arm =>
                HasTargetTyping(source, arm.Expression)
            ),
            BinaryExpressionSyntax binary => HasTargetTyping(source, binary.Left)
                || HasTargetTyping(source, binary.Right),
            PostfixUnaryExpressionSyntax postfix => HasTargetTyping(source, postfix.Operand),
            NameSyntax or MemberAccessExpressionSyntax or MemberBindingExpressionSyntax => source
                .Model.GetSymbolInfo(expression, source.Project.CancellationToken)
                .Symbol is IMethodSymbol,
            _ => false,
        };

    private static bool Validate(
        RewriteContext context,
        AnalysisSource source,
        CodeVariableDeclaration selected
    )
    {
        var before = source
            .Tree.GetRoot(source.Project.CancellationToken)
            .FindNode(selected.Syntax.Span, getInnermostNodeForTie: true);
        if (before.Span != selected.Syntax.Span || before.RawKind != selected.Syntax.RawKind)
            return false;
        var type = WrittenType(before);
        if (
            type is null
            || !Eligible(new(source, before, type))
            || context.Map(source, before) is not { } mapped
        )
            return false;
        return HasValidInference(source, mapped)
            && ScopeBinds(context, source, mapped)
            && RewriteChecks.SameCompilerSuppliedArguments(new(context, mapped, []))
                == ProofResult.Proven
            && PreservesEnclosingBindings(context, source, before);
    }

    private static TypeSyntax? WrittenType(SyntaxNode node) =>
        node switch
        {
            VariableDeclarationSyntax local => local.Type,
            ForEachStatementSyntax loop => loop.Type,
            DeclarationExpressionSyntax expression => expression.Type,
            _ => null,
        };

    private static bool HasValidInference(AnalysisSource source, NodeRewrite mapped)
    {
        var token = source.Project.CancellationToken;
        return WrittenType(mapped.After) is { IsVar: true } afterType
            && mapped.Model.GetAliasInfo(afterType, token) is null
            && mapped.Model.GetSymbolInfo(afterType, token).Symbol
                is not INamedTypeSymbol { Name: "var" }
            && SameType(
                LocalType(source.Model, mapped.Before, token),
                LocalType(mapped.Model, mapped.After, token)
            );
    }

    private static bool ScopeBinds(
        RewriteContext context,
        AnalysisSource source,
        NodeRewrite mapped
    )
    {
        var before = mapped.Before;
        var scope =
            before.AncestorsAndSelf().FirstOrDefault(node => node is MemberDeclarationSyntax)
            ?? before;
        return context.Map(source, scope) is { } changedScope
            && !source
                .Model.GetDiagnostics(scope.Span, source.Project.CancellationToken)
                .Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            && !mapped
                .Model.GetDiagnostics(changedScope.After.Span, source.Project.CancellationToken)
                .Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static bool PreservesEnclosingBindings(
        RewriteContext context,
        AnalysisSource source,
        SyntaxNode before
    )
    {
        foreach (var expression in before.Ancestors().OfType<ExpressionSyntax>())
        {
            if (
                context.Map(source, expression) is not { After: ExpressionSyntax after } enclosing
                || !RewriteSymbols.Same(
                    source.Model.GetSymbolInfo(expression, source.Project.CancellationToken).Symbol,
                    enclosing.Model.GetSymbolInfo(after, source.Project.CancellationToken).Symbol,
                    context
                )
            )
                return false;
        }
        return true;
    }

    private static ITypeSymbol? LocalType(
        SemanticModel model,
        SyntaxNode node,
        CancellationToken cancellationToken
    ) =>
        node switch
        {
            VariableDeclarationSyntax { Variables.Count: 1 } local => (
                model.GetDeclaredSymbol(local.Variables[0], cancellationToken) as ILocalSymbol
            )?.Type,
            ForEachStatementSyntax loop => (
                model.GetDeclaredSymbol(loop, cancellationToken) as ILocalSymbol
            )?.Type,
            DeclarationExpressionSyntax
            {
                Designation: SingleVariableDesignationSyntax designation
            } => (model.GetDeclaredSymbol(designation, cancellationToken) as ILocalSymbol)?.Type,
            _ => null,
        };

    private static bool SameType(ITypeSymbol? before, ITypeSymbol? after) =>
        before is not null
        && after is not null
        && before.TypeKind != TypeKind.Error
        && after.TypeKind != TypeKind.Error
        && before.TypeKind == after.TypeKind
        && before.NullableAnnotation == after.NullableAnnotation
        && before.ContainingAssembly?.Identity.Equals(after.ContainingAssembly?.Identity) != false
        && before.ToDisplayString(_typeFormat) == after.ToDisplayString(_typeFormat)
        && SameTypeComponents(before, after);

    private static bool SameTypeComponents(ITypeSymbol before, ITypeSymbol after) =>
        (before, after) switch
        {
            (IArrayTypeSymbol first, IArrayTypeSymbol second) => SameType(
                first.ElementType,
                second.ElementType
            ),
            (IPointerTypeSymbol first, IPointerTypeSymbol second) => SameType(
                first.PointedAtType,
                second.PointedAtType
            ),
            (INamedTypeSymbol first, INamedTypeSymbol second) => first.TypeArguments.Length
                == second.TypeArguments.Length
                && first
                    .TypeArguments.Zip(second.TypeArguments)
                    .All(pair => SameType(pair.First, pair.Second)),
            _ => true,
        };

    private static bool SameUnderlyingType(ITypeSymbol? before, ITypeSymbol? after) =>
        SameType(
            before?.WithNullableAnnotation(NullableAnnotation.None),
            after?.WithNullableAnnotation(NullableAnnotation.None)
        );
}
