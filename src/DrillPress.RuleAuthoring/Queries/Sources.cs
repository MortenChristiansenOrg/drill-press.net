using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress.Queries;

/// <summary>Reusable roots over ordinary C# source. Generated code supplies semantics but is excluded from these reportable selections.</summary>
public static class Sources
{
    /// <summary>All document memberships for semantic facts, including generated source. The rule evaluator suppresses findings anchored to generated documents.</summary>
    public static CodeQuery<CodeFile> FilesIncludingGenerated { get; } = CodeQuery<CodeFile>.Create(solution =>
        solution.Projects.SelectMany(project => project.Sources).Select(source => new CodeFile(source)));

    /// <summary>All ordinary document memberships, including files containing no type or method.</summary>
    public static CodeQuery<CodeFile> Files { get; } = FilesIncludingGenerated.Where(file => !file.Source.Document.IsGenerated);

    /// <summary>All evaluated projects, including dependencies and alternate frameworks. Project rules should anchor findings to an existing source file.</summary>
    public static CodeQuery<AnalysisProject> Projects { get; } = CodeQuery<AnalysisProject>.Create(solution => solution.Projects);

    /// <summary>Returns a reusable root for any Roslyn syntax type, including declarations, attributes and arguments.</summary>
    public static CodeQuery<CodeNode<TSyntax>> Nodes<TSyntax>() where TSyntax : SyntaxNode => NodeRoot<TSyntax>.Query;

    /// <summary>All C# attribute syntax, available even when binding fails.</summary>
    public static CodeQuery<CodeNode<AttributeSyntax>> Attributes => Nodes<AttributeSyntax>();

    private static class NodeRoot<TSyntax> where TSyntax : SyntaxNode
    {
        internal static readonly CodeQuery<CodeNode<TSyntax>> Query = Files.SelectMany(file => file.Nodes<TSyntax>());
    }
}
