namespace DrillPress.Manifest;

internal sealed record GeneratorCapabilities(
    string ProductVersion,
    int ProtocolVersion,
    GeneratorDescription[] Generators
);

internal sealed record GeneratorProjectPlan(ProjectSnapshot Project, GeneratorInputs Inputs);

internal sealed record GeneratorInputs(
    GeneratorDescription[] Generators,
    GeneratorAdditionalInput[] AdditionalFiles,
    GeneratorAnalyzerConfig[] Configurations,
    string[] DiagnosticIds
);

internal sealed record GeneratorDescription(
    string TypeName,
    string AssemblyName,
    string Fingerprint
);

internal sealed record GeneratorAdditionalInput(
    string Path,
    byte[]? Contents,
    string? EncodingName,
    int ChecksumAlgorithm
);

internal sealed record GeneratorAnalyzerConfig(string Path, string Text);

internal sealed record GeneratorReferenceInput(string ContextId, MetadataReferenceSnapshot? Image);

internal sealed record GeneratorFallbackRequest(
    GeneratorReferenceInput[] References,
    ProjectSnapshot[] SourceContexts
);

internal sealed record GeneratedSourceData(
    int GeneratorIndex,
    string HintName,
    string Path,
    string Text,
    string? EncodingName,
    int ChecksumAlgorithm
);

internal sealed record GeneratorFallbackResponse(GeneratedSourceData[] Sources);

internal sealed record GeneratorBuildEntry(
    GeneratorDescription Description,
    string AssemblyPath,
    bool Incremental,
    bool CanLink
);

internal sealed record GeneratorBuildInventory(
    string ProductVersion,
    GeneratorBuildEntry[] Generators,
    string[] AnalyzerPaths
);
