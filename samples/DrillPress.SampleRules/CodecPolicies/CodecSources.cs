using DrillPress;

namespace DrillPress.SampleRules.CodecPolicies;

// Defines the policy boundary once; all expensive analysis starts from these scoped selections.
internal sealed class CodecSources
{
    private const string Projects = "CodecExamples*";

    internal CodeQuery<CodeFile> Files { get; } = Code.Files.InProject(Projects);
    internal CodeQuery<CodeMethod> Methods { get; } = Code.Methods.InProject(Projects);
    internal CodeQuery<CodeTypeDefinition> Types { get; } = Code.Types.InProject(Projects);
    internal CodeQuery<CodeMethod> TestMethods { get; } = Code.TestMethods.InProject(Projects);
    internal CodeQuery<CodeInvocation> Calls { get; }
    internal CodeQuery<CodeMethod> AsyncMethods { get; }
    internal CodeQuery<CodeTypeDefinition> TextCodecs { get; }
    internal CodeQuery<CodeTypeDeclaration> TypeDeclarations { get; } =
        Code.TypeDeclarations.InProject(Projects);

    internal CodecSources()
    {
        // File-scoped discovery binds only the selected projects.
        Calls = Files.InNonTestProjects().Calls();
        AsyncMethods = Methods.InNonTestProjects().Where(method => method.IsAsync);
        TextCodecs = Types
            .InNonTestProjects()
            .Where(type => !type.IsAbstract && (type.IsClass || type.IsStruct))
            .ImplementingInterface(CodeType.Named("CodecExamples.ITextCodec"));
    }

    internal CodeQuery<CodeFile> NewLegacyFilesSince(SourceBaseline accepted) =>
        Files
            .InFilesNamed("*.Legacy.cs")
            .Where(file => accepted.ChangeOf(file) == SourceChange.Added);

    internal static bool ProjectReferencesNewtonsoftJson(CodeFile file) =>
        file.Source.Project.ReferencesPackage("Newtonsoft.Json");
}
