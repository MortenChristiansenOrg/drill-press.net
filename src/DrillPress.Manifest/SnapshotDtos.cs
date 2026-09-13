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
    string[] MetadataReferences
)
{
    /// <summary>Identifies this evaluated project, target framework, and effective property set.</summary>
    [System.Text.Json.Serialization.JsonRequired]
    public string ContextId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Identifies source project contexts referenced by this compilation.</summary>
    public string[] ReferencedContextIds { get; init; } = [];

    /// <summary>Records the evaluated target framework.</summary>
    public string TargetFramework { get; init; } = "";

    /// <summary>Records effective MSBuild property overrides for this context.</summary>
    public Dictionary<string, string> Properties { get; init; } = [];

    /// <summary>Direct evaluated NuGet references; versions may be ranges or empty when supplied by restore tooling.</summary>
    public PackageReferenceSnapshot[] Packages { get; init; } = [];

    /// <summary>Evaluated source-root items, where the SDK supplies them.</summary>
    public string[] SourceRoots { get; init; } = [];

    /// <summary>External references with validated byte identity and complete binding properties.</summary>
    public MetadataReferenceSnapshot[] ExternalReferences { get; init; } = [];

    /// <summary>Source compilation edges, preserving evaluated framework mappings.</summary>
    public CompilationReferenceSnapshot[] CompilationReferences { get; init; } = [];

    /// <summary>Compiler settings captured from the live compilation.</summary>
    public CompilerOptionsSnapshot CompilerOptions { get; init; } = new();

    /// <summary>Evaluated test-project classification, with inference only when the property is absent.</summary>
    public bool IsTestProject { get; init; }

    /// <summary>The SDK selected relative to the target's global.json.</summary>
    public string SdkVersion { get; init; } = "";

    /// <summary>Contains metadata emitted from project dependencies, including reference aliases.</summary>
    public MetadataImageSnapshot[] ProjectReferences { get; init; } = [];
}

/// <summary>A direct evaluated package reference, distinct from compiler assembly references and transitive restore dependencies.</summary>
/// <param name="Id">NuGet package identity.</param>
/// <param name="Version">Evaluated requested version, including central package versions.</param>
public sealed record PackageReferenceSnapshot(string Id, string Version);

/// <summary>Preserves a project dependency as metadata without requiring a built assembly on disk.</summary>
/// <param name="Image">The emitted metadata assembly.</param>
/// <param name="Aliases">The aliases assigned to this reference in the consuming project.</param>
/// <param name="EmbedInteropTypes">Whether the consuming project embeds interop types from this reference.</param>
public sealed record MetadataImageSnapshot(byte[] Image, string[] Aliases, bool EmbedInteropTypes);

/// <summary>Captures source text together with its physical and generated-source identity.</summary>
/// <param name="Path">The stable physical or generated document path.</param>
/// <param name="Text">The source text passed to the compiler.</param>
/// <param name="IsGenerated">Whether rules must exclude the document from candidate discovery.</param>
public sealed record DocumentSnapshot(string Path, string Text, bool IsGenerated)
{
    /// <summary>Per-tree compiler settings, or the enclosing project's defaults for legacy synthetic inputs.</summary>
    public SourceOptionsSnapshot? Options { get; init; }

    /// <summary>Identifies membership in one compilation context.</summary>
    [System.Text.Json.Serialization.JsonRequired]
    public string DocumentId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Canonical physical identity, separate from the display path.</summary>
    public string FileIdentity { get; init; } = Path;

    /// <summary>Whether the captured source can be replaced without ambiguous aliases.</summary>
    public bool IsEditable { get; init; }

    /// <summary>Original-byte SHA-256 in uppercase hexadecimal, required for editable documents.</summary>
    public string Fingerprint { get; init; } = "";

    /// <summary>Original decoding encoding, required for editable documents.</summary>
    public string EncodingName { get; init; } = "utf-8";

    /// <summary>Whether the original bytes start with that encoding's preamble.</summary>
    public bool HasByteOrderMark { get; init; }
}
