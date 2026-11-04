using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress.SampleRules.Layout;

/// <summary>
/// Names a file after its only top-level type and places a test file in the folder of the production file it
/// tests. Files declaring several types, generic or partial types or top-level statements keep their names,
/// and so do companion files with a dotted name such as <c>MainWindow.xaml.cs</c> and files the SDK's default
/// compile glob does not select: a linked or explicitly listed file is named by its project file.
/// </summary>
internal sealed class FileLayout
{
    private const string TestSuffix = "Tests";

    // The production files of every non-test project by primary type name, for mirroring test folders.
    private readonly AnalysisFact<ILookup<string, CodeFile>> _productionFiles = new(solution =>
        Code.Files.In(solution)
            .Where(file => !file.Source.Project.IsTestProject && file.RelativePath is not null)
            .Where(file => PrimaryType(file) is not null)
            .ToLookup(file => PrimaryType(file)!)
    );

    /// <summary>The file's only top-level type name, or null when it declares none, several, a generic or partial type or top-level statements.</summary>
    internal static string? PrimaryType(CodeFile file)
    {
        if (file.HasTopLevelStatements)
            return null;
        var names = file.Nodes<MemberDeclarationSyntax>()
            .Select(node => node.Syntax)
            .Where(member =>
                member is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax
                && member.Parent is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax
            )
            .Select(member =>
                member switch
                {
                    TypeDeclarationSyntax { TypeParameterList: not null } => null,
                    TypeDeclarationSyntax type
                        when type.Modifiers.Any(modifier => modifier.Text == "partial") => null,
                    DelegateDeclarationSyntax { TypeParameterList: not null } => null,
                    BaseTypeDeclarationSyntax type => type.Identifier.Text,
                    DelegateDeclarationSyntax type => type.Identifier.Text,
                    _ => null,
                }
            )
            .Distinct()
            .ToArray();
        return names is [{ } name] ? name : null;
    }

    /// <summary>Whether the file name differs from its only top-level type; a dotted companion name such as <c>Index.razor.cs</c> is left alone.</summary>
    internal static bool IsMisnamed(CodeFile file) =>
        file.IsDefaultCompileItem
        && file.Name.IndexOf('.') == file.Name.Length - ".cs".Length
        && PrimaryType(file) is { } type
        && file.Name != type + ".cs";

    /// <summary>Renames the file after its type; the library proves that the same projects compile it.</summary>
    internal static FixProposal? Rename(CodeFile file) =>
        Fix.For(file).RenameTo(PrimaryType(file) + ".cs").Propose();

    /// <summary>The path below the test project a <c>&lt;Type&gt;Tests.cs</c> file should have, mirroring the folder of the unique production file declaring <c>&lt;Type&gt;</c> in a referenced project; null when it already has it or none matches.</summary>
    internal string? MirroredPath(CodeFile test)
    {
        if (
            !test.Source.Project.IsTestProject
            || !test.IsDefaultCompileItem
            || test.RelativePath is not { } current
            || PrimaryType(test) is not { } type
            || !type.EndsWith(TestSuffix, StringComparison.Ordinal)
            || test.Name != type + ".cs"
        )
            return null;
        var solution = test.Source.Solution;
        var production = _productionFiles
            .In(solution)[type[..^TestSuffix.Length]]
            .Where(file => solution.ProjectGraph.Includes(test.Source.Project, file.Source.Project))
            .Select(file => file.RelativePath!)
            .Distinct()
            .ToArray();
        if (production is not [var source])
            return null;
        var folder = source.LastIndexOf('/') is var index and >= 0 ? source[..(index + 1)] : "";
        var mirrored = folder + test.Name;
        return mirrored == current ? null : mirrored;
    }

    /// <summary>Moves the test file to the mirrored folder.</summary>
    internal FixProposal? Mirror(CodeFile test) =>
        Fix.For(test).MoveTo(MirroredPath(test)!).Propose();
}
