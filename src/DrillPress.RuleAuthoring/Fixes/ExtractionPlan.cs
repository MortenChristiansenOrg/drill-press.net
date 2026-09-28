using DrillPress.Collections;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress.Fixes;

internal sealed class ExtractionPlan(
    ExpressionGroup group,
    AnalysisSource destination,
    TypeDeclarationSyntax part,
    string name,
    bool reused,
    IReadOnlyList<SourceEdit> edits
)
{
    internal ExpressionGroup Group { get; } = group;
    internal AnalysisSource Destination { get; } = destination;
    internal TypeDeclarationSyntax Part { get; } = part;
    internal string Name { get; } = name;
    internal bool Reused { get; } = reused;

    internal FixProposal Propose(Func<ExtractionEvidence, ProofResult> provesBehavior) =>
        SourceChanges.Propose(
            edits,
            context => ExtractionValidation.Validate(this, context, provesBehavior)
        );

    internal static ExtractionPlan? Create(
        ExpressionGroup group,
        AnalysisSource destination,
        TypeDeclarationSyntax part,
        string name,
        string parameter,
        bool reuse,
        ExtractionNameCollision collision
    )
    {
        if (!Eligible(group, destination, part))
            return null;
        var type = group.Representative.Type ?? group.Representative.TypeInfo.ConvertedType;
        if (type is null || !ExtractionSyntax.Denotable(type))
            return null;
        var selectedName = SelectName(group, type, name, reuse, collision);
        if (selectedName is null)
            return null;
        name = selectedName.Value.Name;
        var reused = selectedName.Value.Reused;
        var qualifier = group.Owner.Symbol.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat
        );
        var access = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.ParseExpression(qualifier),
            SyntaxFactory.IdentifierName(Escape(name))
        );
        var edits = group
            .Occurrences.Select(occurrence =>
                SourceChanges.Replace(
                    occurrence.Expression.Source,
                    occurrence.Expression.Syntax.Span,
                    group.Kind == ExpressionGroupKind.Constant
                        ? access.ToString()
                        : SyntaxFactory
                            .InvocationExpression(
                                access,
                                SyntaxFactory.ArgumentList(
                                    SyntaxFactory.SingletonSeparatedList(
                                        SyntaxFactory.Argument(
                                            occurrence.Capture!.Syntax.WithoutTrivia()
                                        )
                                    )
                                )
                            )
                            .ToString()
                )
            )
            .ToList();
        if (!reused)
        {
            var member = Declaration(group, type, name, parameter);
            if (member is null)
                return null;
            var insertion = ExtractionSyntax.Insert(destination, part, member);
            edits.Add(
                SourceChanges.Replace(
                    destination,
                    new TextSpan(insertion.Position, 0),
                    insertion.Text
                )
            );
        }
        return new(group, destination, part, name, reused, edits.AsReadOnly());
    }

    private static (string Name, bool Reused)? SelectName(
        ExpressionGroup group,
        ITypeSymbol type,
        string name,
        bool reuse,
        ExtractionNameCollision collision
    )
    {
        IFieldSymbol? existing = null;
        if (group.Kind == ExpressionGroupKind.Constant && reuse)
        {
            var matches = group
                .Owner.Symbol.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(field =>
                    field.IsConst
                    && field.HasConstantValue
                    && ExpressionShape.ConstantKey(field.Type, field.ConstantValue)
                        == ExpressionShape.ConstantKey(type, group.Representative.Constant.Value)
                )
                .ToArray();
            existing = matches.FirstOrDefault(field => field.Name == name);
            if (existing is null && matches.Length > 1)
                return null;
            existing ??= matches.SingleOrDefault();
        }
        if (existing is not null)
        {
            name = existing.Name;
            if (
                group.Occurrences.Any(occurrence =>
                    existing.DeclaringSyntaxReferences.Any(reference =>
                        reference.SyntaxTree == occurrence.Expression.Source.Tree
                        && reference.Span.Contains(occurrence.Expression.Syntax.Span)
                    )
                )
            )
                return null;
        }
        else
        {
            var selected = AllocateName(group.Owner.Symbol, name, collision);
            if (selected is null)
                return null;
            name = selected;
        }
        return (name, existing is not null);
    }

    private static bool Eligible(
        ExpressionGroup group,
        AnalysisSource destination,
        TypeDeclarationSyntax part
    )
    {
        if (
            destination.Project != group.Owner.Source.Project
            || !destination.Document.IsEditable
            || destination.Document.IsGenerated
            || part.SyntaxTree != destination.Tree
            || part.ContainsDiagnostics
            || part.ContainsDirectives
            || part.CloseBraceToken.IsMissing
            || part.Modifiers.Any(SyntaxKind.UnsafeKeyword)
            || group.Owner.Symbol.TypeKind is not (TypeKind.Class or TypeKind.Struct)
            || !SymbolEqualityComparer.Default.Equals(
                destination.Model.GetDeclaredSymbol(part),
                group.Owner.Symbol
            )
            || part.CloseBraceToken.LeadingTrivia.Any(trivia =>
                !trivia.IsKind(SyntaxKind.WhitespaceTrivia)
                && !trivia.IsKind(SyntaxKind.EndOfLineTrivia)
            )
        )
            return false;
        foreach (var occurrence in group.Occurrences)
        {
            if (
                !ExtractionSyntax.Eligible(
                    occurrence.Expression.Source,
                    occurrence.Expression.Syntax
                )
                || group.Occurrences.Any(other =>
                    other != occurrence
                    && other.Expression.Source == occurrence.Expression.Source
                    && other.Expression.Syntax.Span.OverlapsWith(occurrence.Expression.Syntax.Span)
                )
            )
                return false;
        }
        return true;
    }

    private static MemberDeclarationSyntax? Declaration(
        ExpressionGroup group,
        ITypeSymbol type,
        string name,
        string parameter
    )
    {
        if (group.Kind == ExpressionGroupKind.Constant)
        {
            var value = ExtractionSyntax.Constant(type, group.Representative.Constant.Value);
            return value is null
                ? null
                : SyntaxFactory
                    .FieldDeclaration(
                        SyntaxFactory
                            .VariableDeclaration(ExtractionSyntax.TypeName(type))
                            .WithVariables(
                                SyntaxFactory.SingletonSeparatedList(
                                    SyntaxFactory
                                        .VariableDeclarator(name)
                                        .WithInitializer(SyntaxFactory.EqualsValueClause(value))
                                )
                            )
                    )
                    .WithModifiers(
                        SyntaxFactory.TokenList(
                            SyntaxFactory.Token(SyntaxKind.PrivateKeyword),
                            SyntaxFactory.Token(SyntaxKind.ConstKeyword)
                        )
                    );
        }
        var capture = group.Occurrences[0].Capture!;
        if (capture.Type is not { } captureType || !ExtractionSyntax.Denotable(captureType))
            return null;
        var body = group
            .Representative.Syntax.ReplaceNode(
                capture.Syntax,
                SyntaxFactory.IdentifierName(parameter)
            )
            .WithoutTrivia();
        return SyntaxFactory
            .MethodDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)),
                name
            )
            .WithModifiers(
                SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PrivateKeyword),
                    SyntaxFactory.Token(SyntaxKind.StaticKeyword)
                )
            )
            .WithParameterList(
                SyntaxFactory.ParameterList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory
                            .Parameter(SyntaxFactory.Identifier(parameter))
                            .WithType(ExtractionSyntax.TypeName(captureType))
                    )
                )
            )
            .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(body))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
    }

    private static string? AllocateName(
        INamedTypeSymbol owner,
        string name,
        ExtractionNameCollision collision
    )
    {
        var used = new HashSet<string>();
        for (var type = owner; type is not null; type = type.BaseType)
            foreach (var member in type.GetMembers())
                used.Add(member.Name);
        if (!used.Contains(name))
            return name;
        return collision == ExtractionNameCollision.AddNumericSuffix
            ? Enumerable
                .Range(1, 1000)
                .Select(index =>
                    name + index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                )
                .FirstOrDefault(candidate => !used.Contains(candidate))
            : null;
    }

    private static string Escape(string name) =>
        SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;
}
