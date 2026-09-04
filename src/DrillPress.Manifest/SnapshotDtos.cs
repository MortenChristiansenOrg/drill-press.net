namespace DrillPress.Manifest;

/// <summary>Captures the compiler inputs needed to reconstruct one evaluated project.</summary>
/// <param name="Name">The evaluated project name.</param>
/// <param name="AssemblyName">The compilation assembly name.</param>
/// <param name="ProjectPath">The physical project file used by BuildHost.</param>
/// <param name="LanguageVersion">The Roslyn language-version value.</param>
/// <param name="OutputKind">The Roslyn compilation output-kind value.</param>
/// <param name="NullableContextOptions">The Roslyn nullable-context value.</param>
/// <param name="PreprocessorSymbols">The symbols active while parsing source.</param>
/// <param name="Documents">The ordinary and generated compilation documents.</param>
/// <param name="MetadataReferences">The physical metadata assemblies used for binding.</param>
public sealed record ProjectSnapshot(
    string Name,
    string AssemblyName,
    string ProjectPath,
    int LanguageVersion,
    int OutputKind,
    int NullableContextOptions,
    string[] PreprocessorSymbols,
    DocumentSnapshot[] Documents,
    string[] MetadataReferences);

/// <summary>Captures source text together with its physical and generated-source identity.</summary>
/// <param name="Path">The stable physical or generated document path.</param>
/// <param name="Text">The source text passed to the compiler.</param>
/// <param name="IsGenerated">Whether rules must exclude the document from candidate discovery.</param>
public sealed record DocumentSnapshot(string Path, string Text, bool IsGenerated);
