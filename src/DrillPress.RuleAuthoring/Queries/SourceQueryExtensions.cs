using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>Discovers candidates only within already selected files, avoiding semantic work in unrelated files.</summary>
public static class SourceQueryExtensions
{
    /// <summary>Selects syntax in these files, including roots and structured trivia.</summary>
    public static CodeQuery<CodeNode<TSyntax>> Nodes<TSyntax>(this CodeQuery<CodeFile> files)
        where TSyntax : SyntaxNode => files.SelectMany(file => file.Nodes<TSyntax>());

    /// <summary>Selects bound calls in these files, including initializers, accessors and lambdas.</summary>
    public static CodeQuery<CodeInvocation> Calls(this CodeQuery<CodeFile> files) =>
        OperationQueries.InvocationsIn(files);

    /// <summary>Selects compiler operations of one kind in these files, including implicit operations.</summary>
    public static CodeQuery<CodeOperation<TOperation>> Operations<TOperation>(
        this CodeQuery<CodeFile> files
    )
        where TOperation : IOperation => OperationQueries.InFiles<TOperation>(files);

    /// <summary>Selects resolved declarations in these files; partial declarations remain separate occurrences.</summary>
    public static CodeQuery<CodeSymbol> Declarations(this CodeQuery<CodeFile> files) =>
        SymbolQueries.DeclarationsIn(files);
}
