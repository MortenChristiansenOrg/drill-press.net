using Microsoft.CodeAnalysis;

namespace DrillPress;

internal static class Sources
{
    internal static CodeQuery<CodeFile> FilesIncludingGenerated { get; } =
        CodeQuery<CodeFile>.Create(solution =>
            solution
                .Projects.SelectMany(project => project.Sources)
                .Select(source => new CodeFile(source))
        );

    internal static CodeQuery<CodeFile> Files { get; } =
        FilesIncludingGenerated.Where(file =>
            !file.Source.Document.IsGenerated && file.Source.Project.Snapshot.IsAnalysisTarget
        );

    internal static CodeQuery<AnalysisProject> Projects { get; } =
        CodeQuery<AnalysisProject>.Create(solution =>
            solution.Projects.Where(project => project.Snapshot.IsAnalysisTarget)
        );

    internal static CodeQuery<CodeNode<TSyntax>> Nodes<TSyntax>()
        where TSyntax : SyntaxNode => NodeRoot<TSyntax>.Query;

    internal static CodeQuery<CodeNode<TSyntax>> InFiles<TSyntax>(CodeQuery<CodeFile> files)
        where TSyntax : SyntaxNode =>
        new(
            (solution, filter) =>
                files
                    .In(solution)
                    .Where(file =>
                        !solution.Options.EnableOptimizations || filter.Includes(file.Source)
                    )
                    .SelectMany(file => file.Nodes<TSyntax>())
        );

    private static class NodeRoot<TSyntax>
        where TSyntax : SyntaxNode
    {
        internal static readonly CodeQuery<CodeNode<TSyntax>> Query = InFiles<TSyntax>(Files);
    }
}
