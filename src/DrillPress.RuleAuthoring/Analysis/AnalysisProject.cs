using System.IO.Abstractions;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress;

/// <summary>One evaluated compilation context; alternate target frameworks remain separate.</summary>
public sealed class AnalysisProject
{
    private readonly Lazy<IReadOnlyList<AnalysisSource>> _sources;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        CodeType,
        TypeAvailability
    > _types = new();

    /// <summary>Tests unique contextual type availability, including transitive references and extern aliases; does not imply source usage.</summary>
    public bool HasType(CodeType type) =>
        InspectType(type).Status == TypeAvailabilityStatus.Available;

    /// <summary>Resolves a complete configured identity once per evaluated context, retaining missing and ambiguous outcomes.</summary>
    public TypeAvailability InspectType(CodeType type)
    {
        CancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(type.MetadataName);
        return _types.GetOrAdd(type, identity => identity.InspectAvailability(Compilation));
    }

    /// <summary>Pairs a compiler-faithful snapshot with its live or reconstructed compilation.</summary>
    public AnalysisProject(
        ProjectSnapshot snapshot,
        CSharpCompilation compilation,
        CancellationToken cancellationToken = default
    )
        : this(snapshot, compilation, new FileSystem(), cancellationToken) { }

    internal AnalysisProject(
        ProjectSnapshot snapshot,
        CSharpCompilation compilation,
        IFileSystem fileSystem,
        CancellationToken cancellationToken = default
    )
    {
        FileSystem = fileSystem;
        InvocationDirectory = fileSystem.Directory.GetCurrentDirectory();
        var trees = compilation.SyntaxTrees.ToArray();
        if (
            trees.Length != snapshot.Documents.Length
            || trees
                .Where(
                    (tree, index) =>
                        !snapshot.Documents[index].IsGenerated
                        && tree.FilePath != snapshot.Documents[index].Path
                )
                .Any()
        )
        {
            throw new ArgumentException(
                "Compilation trees must match the snapshot's ordered document memberships.",
                nameof(compilation)
            );
        }

        RewriteValidation = new(this);
        CancellationToken = cancellationToken;
        Snapshot = snapshot;
        Compilation = compilation;
        _sources = new(() =>
            trees
                .Zip(
                    snapshot.Documents,
                    (tree, document) => new AnalysisSource(this, document, tree)
                )
                .ToArray()
        );
    }

    internal ProjectCoverage Coverage { get; set; } = new();

    internal RewriteValidationCache RewriteValidation { get; }

    internal IFileSystem FileSystem { get; }

    internal string InvocationDirectory { get; }

    /// <summary>Stops candidate discovery and contextual compiler validation.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Evaluated context identity, graph edges, and document metadata.</summary>
    public ProjectSnapshot Snapshot { get; }

    /// <summary>The evaluated project name, suitable for configured project roles.</summary>
    public string Name => Snapshot.Name;

    /// <summary>The captured project path; use it with the framework and evaluation properties to distinguish contexts.</summary>
    public string ProjectPath => Snapshot.ProjectPath;

    /// <summary>The evaluated target framework; loose source uses an empty value.</summary>
    public string TargetFramework => Snapshot.TargetFramework;

    /// <summary>BuildHost's evaluated or inferred test-project classification.</summary>
    public bool IsTestProject => Snapshot.IsTestProject;

    /// <summary>Direct package references, including requested central versions where available.</summary>
    public IReadOnlyList<PackageReferenceSnapshot> Packages => Array.AsReadOnly(Snapshot.Packages);

    /// <summary>Captured policy properties and explicit MSBuild overrides; this is not the complete environment or evaluated property bag.</summary>
    public IReadOnlyDictionary<string, string> Properties =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(Snapshot.Properties);

    /// <summary>Source-root items supplied by MSBuild, which may be empty.</summary>
    public IReadOnlyList<string> SourceRoots => Array.AsReadOnly(Snapshot.SourceRoots);

    /// <summary>The compiler used for semantic identity and contextual rewrite validation.</summary>
    public CSharpCompilation Compilation { get; }

    /// <summary>Lazily pairs every syntax tree, including generated trees, with its captured source.</summary>
    public IReadOnlyList<AnalysisSource> Sources => _sources.Value;
}
