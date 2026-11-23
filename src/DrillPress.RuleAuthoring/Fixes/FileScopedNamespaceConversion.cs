using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

/// <summary>Converts a file's single block namespace and removes one indentation level without reformatting its contents.</summary>
/// <remarks>The rule must exclude source exported or concatenated into another compilation with different namespace constraints.</remarks>
public sealed class FileScopedNamespaceConversion
{
    private readonly AnalysisSource _source;
    private readonly NamespaceDeclarationSyntax _declaration;

    internal FileScopedNamespaceConversion(CodeNode<NamespaceDeclarationSyntax> declaration)
    {
        _source = declaration.Source;
        _declaration = declaration.Syntax;
    }

    /// <summary>Proposes a file-scoped namespace, retaining declared type names, string values and compiler-supplied arguments such as caller line numbers. Nested namespaces, directives inside the namespace other than <c>#nullable</c>, <c>#pragma warning</c> and <c>#region</c>, multiline text other than raw strings and ambiguous indentation withhold the fix with a reason.</summary>
    public FixProposal Propose() => Convert(_source, _declaration);

    private static FixProposal Convert(
        AnalysisSource source,
        NamespaceDeclarationSyntax declaration
    )
    {
        if (declaration.Parent is not CompilationUnitSyntax unit)
            return FixProposal.Withheld("the namespace is nested in another namespace");
        if (unit.Members.Count != 1)
            return FixProposal.Withheld("the file declares other top-level members or namespaces");
        if (declaration.Members.Any(member => member is BaseNamespaceDeclarationSyntax))
            return FixProposal.Withheld("the namespace contains a nested namespace");
        var text = source.Tree.GetText(source.Project.CancellationToken);
        var open = declaration.OpenBraceToken;
        var close = declaration.CloseBraceToken;
        var bodyStart = text.Lines.GetLineFromPosition(open.Span.End).LineNumber + 1;
        var bodyEnd = text.Lines.GetLineFromPosition(close.SpanStart).LineNumber - 1;
        var closeLine = text.Lines[bodyEnd + 1];
        var openLine = text.Lines[bodyStart - 1];
        var lineBreak = text.ToString(
            TextSpan.FromBounds(openLine.End, openLine.EndIncludingLineBreak)
        );
        if (
            declaration
                .Name.GetTrailingTrivia()
                .Concat(open.LeadingTrivia)
                .Any(trivia =>
                    !trivia.IsKind(SyntaxKind.WhitespaceTrivia)
                    && !trivia.IsKind(SyntaxKind.EndOfLineTrivia)
                )
        )
            return FixProposal.Withheld(
                "a comment or directive sits between the namespace name and its brace"
            );
        if (text.ToString(TextSpan.FromBounds(open.Span.End, openLine.End)).Trim().Length > 0)
            return FixProposal.Withheld("code follows the namespace's opening brace on its line");
        if (text.ToString(closeLine.Span).Trim() != "}")
            return FixProposal.Withheld("the namespace's closing brace shares its line");
        if (lineBreak.Length == 0 || bodyStart > bodyEnd)
            return FixProposal.Withheld("the namespace body is empty");
        if (UnsafeDirective(unit, declaration) is { } directive)
            return FixProposal.Withheld(
                $"the conversion does not support the directive `{FixReasons.Text(directive.ToString())}` at {FixReasons.Position(source, directive.SpanStart)}"
            );
        var indentation = IndentationOf(text, bodyStart, bodyEnd);
        if (indentation.Length == 0)
            return FixProposal.Withheld(
                "the namespace body is not indented by at least four characters"
            );

        var dedented = Enumerable
            .Range(bodyStart, bodyEnd - bodyStart + 1)
            .Select(line => text.Lines[line].Span)
            .Where(span =>
                span.Length >= indentation.Length
                && text.ToString(new(span.Start, indentation.Length)) == indentation
            )
            .ToArray();
        if (ChangingText(declaration, text, dedented) is { } changing)
            return FixProposal.Withheld(
                $"`{FixReasons.Text(changing.Text)}` at {FixReasons.Position(source, changing.SpanStart)} is multiline text whose value the indentation change would alter"
            );

        var edits = new List<SourceEdit>
        {
            // From the end of the name to the opening brace: "namespace A;", then a blank line ending in the brace's line break.
            SourceChanges.Replace(
                source,
                TextSpan.FromBounds(declaration.Name.Span.End, open.Span.End),
                ";" + lineBreak
            ),
        };
        edits.AddRange(
            dedented.Select(span =>
                SourceChanges.Replace(source, new(span.Start, indentation.Length), "")
            )
        );
        edits.Add(SourceChanges.Replace(source, closeLine.SpanIncludingLineBreak, ""));
        var declared = DeclaredTypeNames(source.Model, declaration);
        var strings = StringTokens(declaration);
        return SourceChanges.ProposeExplained(
            edits,
            context => Validate(context, source, declaration, declared, strings, edits)
        );
    }

    // The same namespace declaring the same types, every string keeping its value, and every compiler-supplied
    // argument, such as a caller line number, unchanged.
    private static string? Validate(
        RewriteContext context,
        AnalysisSource source,
        NamespaceDeclarationSyntax declaration,
        List<string> declared,
        List<SyntaxToken> strings,
        IReadOnlyList<SourceEdit> edits
    )
    {
        if (
            context.TreeFor(source) is not { } tree
            || tree.GetRoot()
                .DescendantNodes()
                .OfType<FileScopedNamespaceDeclarationSyntax>()
                .SingleOrDefault()
                is not { } after
            || after.Name.ToString() != declaration.Name.ToString()
        )
            return "the edit does not produce the same file-scoped namespace";
        var model = context.Rewritten.GetSemanticModel(tree);
        if (!DeclaredTypeNames(model, after).SequenceEqual(declared))
            return "the file-scoped namespace does not declare the same types";
        var values = StringTokens(after);
        if (values.Count != strings.Count)
            return "the file-scoped namespace does not contain the same strings";
        foreach (var (before, rewritten) in strings.Zip(values))
            if (before.ValueText != rewritten.ValueText)
                return $"`{FixReasons.Text(before.Text)}` at {FixReasons.Position(source, before.SpanStart)} changes its value with the indentation";
        var evidence = new RewriteEvidence(
            context,
            new(source, source.Tree.GetRoot(), tree.GetRoot(), model),
            [],
            edits
        );
        return RewriteChecks.CompilerSuppliedArguments(evidence, null).Reason;
    }

    // Removing indentation keeps the meaning of these directives. Others from the namespace on, such as #if, #line
    // or #error, withhold the conversion: an inactive branch could declare another namespace or member. Those
    // among the usings stay where they are.
    private static SyntaxTrivia? UnsafeDirective(
        CompilationUnitSyntax unit,
        NamespaceDeclarationSyntax declaration
    ) =>
        unit.DescendantTrivia(TextSpan.FromBounds(declaration.FullSpan.Start, unit.FullSpan.End))
            .Where(trivia =>
                trivia.IsDirective
                && trivia.SpanStart >= declaration.FullSpan.Start
                && trivia.Kind()
                    is not (
                        SyntaxKind.NullableDirectiveTrivia
                        or SyntaxKind.PragmaWarningDirectiveTrivia
                        or SyntaxKind.RegionDirectiveTrivia
                        or SyntaxKind.EndRegionDirectiveTrivia
                        or SyntaxKind.DefineDirectiveTrivia
                        or SyntaxKind.UndefDirectiveTrivia
                    )
            )
            .Select(trivia => (SyntaxTrivia?)trivia)
            .FirstOrDefault();

    // Raw strings remove the closing line's indentation from every line, so dedenting all of their lines keeps
    // their value; the proof compares every string's value. Other multiline text keeps the indentation of its later
    // lines, so it must not lose any.
    private static SyntaxToken? ChangingText(
        NamespaceDeclarationSyntax declaration,
        SourceText text,
        TextSpan[] dedented
    )
    {
        foreach (var token in declaration.DescendantTokens())
        {
            if (!IsStringText(token) || !token.Text.Contains('\n') || IsRaw(token))
                continue;
            var firstLineEnd = text.Lines.GetLineFromPosition(token.SpanStart).End;
            if (dedented.Any(span => span.Start > firstLineEnd && span.Start < token.Span.End))
                return token;
        }

        return null;
    }

    private static bool IsRaw(SyntaxToken token) =>
        token.Kind()
            is SyntaxKind.MultiLineRawStringLiteralToken
                or SyntaxKind.Utf8MultiLineRawStringLiteralToken
        || token.Parent?.Parent
            is InterpolatedStringExpressionSyntax
            {
                StringStartToken.RawKind: (int)SyntaxKind.InterpolatedMultiLineRawStringStartToken,
            };

    private static List<SyntaxToken> StringTokens(SyntaxNode root) =>
        root.DescendantTokens().Where(IsStringText).ToList();

    private static bool IsStringText(SyntaxToken token) =>
        token.Kind()
            is SyntaxKind.StringLiteralToken
                or SyntaxKind.Utf8StringLiteralToken
                or SyntaxKind.SingleLineRawStringLiteralToken
                or SyntaxKind.Utf8SingleLineRawStringLiteralToken
                or SyntaxKind.MultiLineRawStringLiteralToken
                or SyntaxKind.Utf8MultiLineRawStringLiteralToken
                or SyntaxKind.InterpolatedStringTextToken;

    private static List<string> DeclaredTypeNames(SemanticModel model, SyntaxNode root) =>
        root.DescendantNodes()
            .OfType<BaseTypeDeclarationSyntax>()
            .Select(type => model.GetDeclaredSymbol(type)?.ToDisplayString() ?? "")
            .ToList();

    private static string IndentationOf(SourceText text, int first, int last)
    {
        for (var line = first; line <= last; line++)
        {
            // Directives may start in the first column.
            var content = text.ToString(text.Lines[line].Span);
            if (content.Trim().Length > 0 && !content.TrimStart().StartsWith('#'))
            {
                return
                    content[..(content.Length - content.TrimStart().Length)]
                        is { Length: >= 4 } indentation
                    ? indentation[..4]
                    : "";
            }
        }
        return "";
    }
}
