using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

    /// <summary>Proposes a file-scoped namespace, retaining declared type names, string values and compiler-supplied arguments such as caller line numbers. Nested namespaces, directives, multiline text and ambiguous indentation withhold the fix.</summary>
    public FixProposal? Propose() => Convert(_source, _declaration);

    private static FixProposal? Convert(
        AnalysisSource source,
        NamespaceDeclarationSyntax declaration
    )
    {
        if (
            declaration.Parent is not CompilationUnitSyntax unit
            || unit.Members.Count != 1
            || declaration.Members.Any(member => member is BaseNamespaceDeclarationSyntax)
            || declaration.DescendantTokens().Any(token => IsMultiLineText(token))
            || declaration.DescendantTrivia().Any(trivia => trivia.IsDirective)
        )
        {
            return null;
        }
        var text = source.Tree.GetText(source.Project.CancellationToken);
        var open = declaration.OpenBraceToken;
        var close = declaration.CloseBraceToken;
        var bodyStart = text.Lines.GetLineFromPosition(open.Span.End).LineNumber + 1;
        var bodyEnd = text.Lines.GetLineFromPosition(close.SpanStart).LineNumber - 1;
        var closeLine = text.Lines[bodyEnd + 1];
        var openLine = text.Lines[bodyStart - 1];
        var lineBreak = text.ToString(
            Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(
                openLine.End,
                openLine.EndIncludingLineBreak
            )
        );
        if (
            declaration
                .Name.GetTrailingTrivia()
                .Any(trivia =>
                    !trivia.IsKind(SyntaxKind.WhitespaceTrivia)
                    && !trivia.IsKind(SyntaxKind.EndOfLineTrivia)
                )
            || text.ToString(
                    Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(open.Span.End, openLine.End)
                )
                .Trim()
                .Length > 0
            || text.ToString(closeLine.Span).Trim() != "}"
            || lineBreak.Length == 0
            || bodyStart > bodyEnd
        )
        {
            return null;
        }
        var indentation = IndentationOf(text, bodyStart, bodyEnd);
        if (indentation.Length == 0)
        {
            return null;
        }
        var edits = new List<SourceEdit>
        {
            // From the end of the name to the opening brace: "namespace A;", then a blank line ending in the brace's line break.
            SourceChanges.Replace(
                source,
                Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(
                    declaration.Name.Span.End,
                    open.Span.End
                ),
                ";" + lineBreak
            ),
        };
        for (var line = bodyStart; line <= bodyEnd; line++)
        {
            var span = text.Lines[line].Span;
            if (
                span.Length >= indentation.Length
                && text.ToString(new(span.Start, indentation.Length)) == indentation
            )
            {
                edits.Add(SourceChanges.Replace(source, new(span.Start, indentation.Length), ""));
            }
        }
        edits.Add(SourceChanges.Replace(source, closeLine.SpanIncludingLineBreak, ""));
        var declared = DeclaredTypeNames(source.Model, declaration);
        var strings = StringValues(declaration);
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
        List<string> strings,
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
        if (!StringValues(after).SequenceEqual(strings))
            return "a string's value changes with the indentation";
        var evidence = new RewriteEvidence(
            context,
            new(source, source.Tree.GetRoot(), tree.GetRoot(), model),
            [],
            edits
        );
        return RewriteChecks.CompilerSuppliedArguments(evidence, null).Reason;
    }

    private static List<string> StringValues(SyntaxNode root) =>
        root.DescendantTokens().Where(IsStringText).Select(token => token.ValueText).ToList();

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

    private static string IndentationOf(
        Microsoft.CodeAnalysis.Text.SourceText text,
        int first,
        int last
    )
    {
        for (var line = first; line <= last; line++)
        {
            var content = text.ToString(text.Lines[line].Span);
            if (content.Trim().Length > 0)
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

    private static bool IsMultiLineText(SyntaxToken token) =>
        (IsStringText(token) || token.IsKind(SyntaxKind.InterpolatedMultiLineRawStringStartToken))
        && token.Text.Contains('\n');
}
