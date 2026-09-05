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
    string[] MetadataReferences)
{
    /// <summary>Contains metadata emitted from project dependencies, including reference aliases.</summary>
    public MetadataImageSnapshot[] ProjectReferences { get; init; } = [];
}

/// <summary>Preserves a project dependency as metadata without requiring a built assembly on disk.</summary>
/// <param name="Image">The emitted metadata assembly.</param>
/// <param name="Aliases">The aliases assigned to this reference in the consuming project.</param>
/// <param name="EmbedInteropTypes">Whether the consuming project embeds interop types from this reference.</param>
public sealed record MetadataImageSnapshot(byte[] Image, string[] Aliases, bool EmbedInteropTypes);

/// <summary>Captures source text together with its physical and generated-source identity.</summary>
/// <param name="Path">The stable physical or generated document path.</param>
/// <param name="Text">The source text passed to the compiler.</param>
/// <param name="IsGenerated">Whether rules must exclude the document from candidate discovery.</param>
public sealed record DocumentSnapshot(string Path, string Text, bool IsGenerated);
