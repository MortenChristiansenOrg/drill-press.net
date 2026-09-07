namespace DrillPress.Manifest;

/// <summary>Preserves external assembly identity and binding properties.</summary>
/// <param name="Path">Absolute external metadata path.</param>
/// <param name="Fingerprint">SHA-256 of the captured bytes.</param>
/// <param name="Aliases">Compiler reference aliases, including global when present.</param>
/// <param name="EmbedInteropTypes">Whether interop types are embedded.</param>
/// <param name="Kind">Roslyn metadata image kind.</param>
public sealed record MetadataReferenceSnapshot(string Path, string Fingerprint, string[] Aliases, bool EmbedInteropTypes, int Kind);

/// <summary>Connects a consuming compilation to a specific evaluated source context.</summary>
/// <param name="ContextId">Referenced compilation identity.</param>
/// <param name="Aliases">Aliases used by the consuming compilation.</param>
/// <param name="EmbedInteropTypes">Whether interop types are embedded.</param>
public sealed record CompilationReferenceSnapshot(string ContextId, string[] Aliases, bool EmbedInteropTypes);

/// <summary>Binding and diagnostic settings beyond the basic compilation contract.</summary>
public sealed record CompilerOptionsSnapshot
{
    /// <summary>Metadata visibility imported by the compiler.</summary>
    public int MetadataImportOptions { get; init; }
    /// <summary>Whether desktop assembly unification rules apply.</summary>
    public bool DesktopAssemblyIdentity { get; init; }
    /// <summary>Compiler optimization mode.</summary>
    public int OptimizationLevel { get; init; }
    /// <summary>Target processor architecture.</summary>
    public int Platform { get; init; }
    /// <summary>Enables unsafe source constructs.</summary>
    public bool AllowUnsafe { get; init; }
    /// <summary>Enables overflow checking by default.</summary>
    public bool CheckOverflow { get; init; }
    /// <summary>Compiler warning level.</summary>
    public int WarningLevel { get; init; } = 4;
    /// <summary>Default diagnostic reporting policy.</summary>
    public int GeneralDiagnosticOption { get; init; }
    /// <summary>Diagnostic-specific reporting policies.</summary>
    public Dictionary<string, int> SpecificDiagnosticOptions { get; init; } = [];
    /// <summary>Whether suppressed diagnostics are retained.</summary>
    public bool ReportSuppressedDiagnostics { get; init; }
    /// <summary>Deterministic compilation identity policy.</summary>
    public bool Deterministic { get; init; }
    /// <summary>Entry-point type, when explicitly selected.</summary>
    public string? MainTypeName { get; init; }
    /// <summary>Explicit output module name.</summary>
    public string? ModuleName { get; init; }
    /// <summary>Top-level script container name.</summary>
    public string? ScriptClassName { get; init; } = "Script";
    /// <summary>Global namespace imports.</summary>
    public string[] Usings { get; init; } = [];
    /// <summary>Assembly public key; private signing inputs are never exported.</summary>
    public byte[] CryptoPublicKey { get; init; } = [];
    /// <summary>Public-sign policy.</summary>
    public bool PublicSign { get; init; }
    /// <summary>Delay-sign policy.</summary>
    public bool? DelaySign { get; init; }
}

/// <summary>Per-source parse settings and effective analyzer-config compiler severities.</summary>
public sealed record SourceOptionsSnapshot
{
    /// <summary>Language version for this syntax tree.</summary>
    public int LanguageVersion { get; init; }
    /// <summary>Documentation comment parsing mode.</summary>
    public int DocumentationMode { get; init; } = 1;
    /// <summary>Regular or script source kind.</summary>
    public int Kind { get; init; }
    /// <summary>Conditional compilation symbols.</summary>
    public string[] PreprocessorSymbols { get; init; } = [];
    /// <summary>Compiler feature flags.</summary>
    public Dictionary<string, string> Features { get; init; } = [];
    /// <summary>Effective compiler severity overrides for this physical tree.</summary>
    public Dictionary<string, int> DiagnosticOptions { get; init; } = [];
}
