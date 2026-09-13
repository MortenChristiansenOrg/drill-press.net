using DrillPress.Operations;
using Microsoft.CodeAnalysis;

namespace DrillPress.Queries;

/// <summary>Discovers compiler-backed candidates from an already selected file scope.</summary>
public static class SourceQueryExtensions
{
    /// <summary>Selects syntax in these files, including roots and structured trivia, without scanning unrelated files.</summary>
    public static CodeQuery<CodeNode<TSyntax>> Nodes<TSyntax>(this CodeQuery<CodeFile> files)
        where TSyntax : SyntaxNode => files.SelectMany(file => file.Nodes<TSyntax>());

    /// <summary>Selects bound calls in these files, including initializers, accessors and nested functions.</summary>
    public static CodeQuery<CodeInvocation> Invocations(this CodeQuery<CodeFile> files) =>
        OperationQueries.InvocationsIn(files);

    /// <summary>Selects resolved declarations in these files; partial declarations remain separate occurrences.</summary>
    public static CodeQuery<CodeSymbol> Declarations(this CodeQuery<CodeFile> files) =>
        SymbolQueries.DeclarationsIn(files);
}
