using DrillPress.Baselines;
using DrillPress.Operations;
using DrillPress.Projects;
using DrillPress.Queries;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress.SampleRules.CodecPolicies;

// Defines the policy boundary once; all expensive analysis starts from these scoped selections.
internal sealed class CodecSources
{
    internal CodeQuery<CodeFile> Files { get; } =
        Sources.Files.Where(file => IsCodecProject(file.Source.Project));
    internal CodeQuery<CodeMethod> Methods { get; } =
        Code.Methods.Where(method => IsCodecProject(method.Source.Project));
    internal CodeQuery<CodeDeclaration> Types { get; } =
        Code.Types.Where(type => IsCodecProject(type.Source.Project));
    internal CodeQuery<CodeInvocation> Calls { get; }
    internal CodeQuery<CodeMethod> AsyncMethods { get; }
    internal CodeQuery<CodeDeclaration> TextCodecs { get; }
    internal CodeQuery<CodeNode<MemberDeclarationSyntax>> TopLevelTypes { get; }
    internal CodeQuery<CodeSymbol> TypeDeclarations { get; }

    internal CodecSources()
    {
        Calls = OperationQueries.InvocationsIn(Files);
        AsyncMethods = Methods.Where(method => method.IsAsync);
        TextCodecs = Types.Where(type =>
            type.Implements(CodeType.Named("CodecExamples.ITextCodec"))
        );
        TopLevelTypes = Files
            .SelectMany(file => file.Nodes<MemberDeclarationSyntax>())
            .Where(IsTopLevelType);
        TypeDeclarations = SymbolQueries
            .DeclarationsIn(Files)
            .Where(declaration => declaration.Symbol is INamedTypeSymbol);
    }

    internal CodeQuery<CodeFile> NewLegacyFilesSince(SourceBaseline accepted) =>
        Files
            .Where(file => accepted.ChangeOf(file) == SourceChange.Added)
            .Where(file => file.Name.AsSpan().EndsWith(".Legacy.cs"));

    internal static bool ProjectReferencesNewtonsoftJson(CodeFile file) =>
        file.Source.Project.ReferencesPackage("Newtonsoft.Json");

    internal static bool DeclaresInternalAccessibility(
        CodeNode<MemberDeclarationSyntax> declaration
    ) => declaration.Syntax.Modifiers.Any(SyntaxKind.InternalKeyword);

    internal static bool HasSerializableAttribute(CodeSymbol declaration) =>
        Symbols.HasAttribute(declaration.Symbol, CodeType.Named("System.SerializableAttribute"));

    private static bool IsCodecProject(AnalysisProject project) =>
        project.Name.AsSpan().StartsWith("CodecExamples");

    private static bool IsTopLevelType(CodeNode<MemberDeclarationSyntax> declaration) =>
        declaration.Syntax
            is TypeDeclarationSyntax
            {
                Parent: CompilationUnitSyntax or BaseNamespaceDeclarationSyntax
            };
}
