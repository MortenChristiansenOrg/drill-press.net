using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Security.Cryptography;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress.Engine;

internal sealed class SnapshotCompiler(IFileSystem fileSystem, bool revalidateReferences = true)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly Dictionary<
        string,
        (PortableExecutableReference Metadata, string Fingerprint)
    > _references = [];
    private readonly HashSet<string> _verifiedPaths = [];
    private string? _cachedFingerprint;
    private CompilationContext[]? _cachedContexts;
    private readonly Dictionary<
        string,
        (string Fingerprint, CompilationContext Context)
    > _projects = [];

    // Streaming passes the same immutable project snapshots on every round; hashing each once keeps
    // the rounds linear in the solution's size.
    private readonly Dictionary<ProjectSnapshot, byte[]> _contentHashes = new(
        ReferenceEqualityComparer.Instance
    );

    public CompilationContext[] Reconstruct(
        CompilationSnapshot snapshot,
        CancellationToken cancellationToken
    )
    {
        SnapshotValidation.Validate(snapshot);
        if (revalidateReferences)
            _verifiedPaths.Clear();
        foreach (var project in snapshot.Projects)
        {
            foreach (var reference in project.ExternalReferences)
                _ = ReadReference(reference);
            foreach (var path in project.MetadataReferences)
                _ = ReadReference(new MetadataReferenceSnapshot(path, "", [], false, 0));
        }
        var fingerprint = Fingerprint(snapshot);
        if (_cachedFingerprint == fingerprint && _cachedContexts is not null)
            return _cachedContexts;
        var projects = snapshot.Projects.ToDictionary(project => project.ContextId);
        var signatures = ProjectFingerprints(projects, cancellationToken);
        var parsed = PrepareTrees(
            snapshot with
            {
                Projects = snapshot
                    .Projects.Where(project =>
                        !_projects.TryGetValue(project.ContextId, out var previous)
                        || previous.Fingerprint != signatures[project.ContextId]
                    )
                    .ToArray(),
            },
            cancellationToken
        );
        var completed = new Dictionary<string, CompilationContext>();
        var visiting = new HashSet<string>();
        foreach (var project in snapshot.Projects)
        {
            Visit(project);
        }

        var contexts = snapshot.Projects.Select(project => completed[project.ContextId]).ToArray();
        _projects.Clear();
        foreach (var context in contexts)
            _projects.Add(
                context.Snapshot.ContextId,
                (signatures[context.Snapshot.ContextId], context)
            );
        _cachedFingerprint = fingerprint;
        _cachedContexts = contexts;
        return contexts;

        CompilationContext Visit(ProjectSnapshot project)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (completed.TryGetValue(project.ContextId, out var existing))
            {
                return existing;
            }
            if (
                _projects.TryGetValue(project.ContextId, out var previous)
                && previous.Fingerprint == signatures[project.ContextId]
            )
            {
                completed.Add(project.ContextId, previous.Context);
                return previous.Context;
            }

            if (!visiting.Add(project.ContextId))
            {
                throw new InvalidDataException("The source compilation graph contains a cycle.");
            }

            var references = project
                .CompilationReferences.Select(reference =>
                {
                    if (!projects.TryGetValue(reference.ContextId, out var dependency))
                    {
                        throw new InvalidDataException("Unknown source compilation reference.");
                    }

                    return (MetadataReference)
                        Visit(dependency)
                            .Compilation.ToMetadataReference(
                                reference.Aliases.ToImmutableArray(),
                                reference.EmbedInteropTypes
                            );
                })
                .ToList();
            references.AddRange(project.ExternalReferences.Select(ReadReference));
            references.AddRange(
                project.MetadataReferences.Select(path =>
                    ReadReference(new MetadataReferenceSnapshot(path, "", [], false, 0))
                )
            );
            references.AddRange(
                project.ProjectReferences.Select(reference =>
                    MetadataReference.CreateFromImage(
                        reference.Image,
                        MetadataReferenceProperties
                            .Assembly.WithAliases(reference.Aliases)
                            .WithEmbedInteropTypes(reference.EmbedInteropTypes)
                    )
                )
            );
            var trees = parsed[project.ContextId].Select(tree => tree.Value).ToArray();
            var options = CreateOptions(project)
                .WithSyntaxTreeOptionsProvider(
                    new SnapshotTreeOptions(trees.Zip(project.Documents).ToDictionary())
                );
            var compilation = CSharpCompilation.Create(
                project.AssemblyName,
                trees,
                references,
                options
            );
            var result = new CompilationContext(project, compilation);
            completed.Add(project.ContextId, result);
            visiting.Remove(project.ContextId);
            return result;
        }
    }

    private Dictionary<string, string> ProjectFingerprints(
        IReadOnlyDictionary<string, ProjectSnapshot> projects,
        CancellationToken cancellationToken
    )
    {
        var completed = new Dictionary<string, string>();
        var visiting = new HashSet<string>();
        foreach (var project in projects.Values)
            Visit(project);
        return completed;

        string Visit(ProjectSnapshot project)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (completed.TryGetValue(project.ContextId, out var fingerprint))
                return fingerprint;
            if (!visiting.Add(project.ContextId))
                throw new InvalidDataException("The source compilation graph contains a cycle.");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(ContentHash(project));
            foreach (var reference in project.CompilationReferences)
                hash.AppendData(
                    System.Text.Encoding.UTF8.GetBytes(Visit(projects[reference.ContextId]))
                );
            foreach (var path in project.MetadataReferences)
                hash.AppendData(System.Text.Encoding.UTF8.GetBytes(_references[path].Fingerprint));
            visiting.Remove(project.ContextId);
            fingerprint = Convert.ToHexString(hash.GetHashAndReset());
            completed.Add(project.ContextId, fingerprint);
            return fingerprint;
        }
    }

    private byte[] ContentHash(ProjectSnapshot project)
    {
        if (!_contentHashes.TryGetValue(project, out var hash))
        {
            hash = SHA256.HashData(
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                    project,
                    CompilationSnapshotJsonContext.Default.ProjectSnapshot
                )
            );
            _contentHashes.Add(project, hash);
        }
        return hash;
    }

    private string Fingerprint(CompilationSnapshot snapshot)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                snapshot with
                {
                    RequestId = "",
                    Projects = [],
                },
                CompilationSnapshotJsonContext.Default.CompilationSnapshot
            )
        );
        foreach (var project in snapshot.Projects)
            hash.AppendData(ContentHash(project));
        foreach (var path in _verifiedPaths.Order(StringComparer.Ordinal))
            hash.AppendData(
                System.Text.Encoding.UTF8.GetBytes(path + "\n" + _references[path].Fingerprint)
            );
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static Dictionary<string, Lazy<SyntaxTree>[]> PrepareTrees(
        CompilationSnapshot snapshot,
        CancellationToken cancellationToken
    )
    {
        var parsed = snapshot.Projects.ToDictionary(
            project => project.ContextId,
            project =>
                project
                    .Documents.Select(document => new Lazy<SyntaxTree>(() =>
                        CreateTree(project, document, cancellationToken)
                    ))
                    .ToArray()
        );
        Parallel.ForEach(
            parsed.Values.SelectMany(trees => trees),
            new ParallelOptions { CancellationToken = cancellationToken },
            tree =>
            {
                try
                {
                    _ = tree.Value;
                }
                catch (Exception)
                {
                    // The ordered reader observes the cached failure in snapshot order.
                }
            }
        );
        return parsed;
    }

    private PortableExecutableReference ReadReference(MetadataReferenceSnapshot reference)
    {
        _references.TryGetValue(reference.Path, out var captured);
        if (_verifiedPaths.Add(reference.Path))
        {
            using var input = _fileSystem.File.OpenRead(reference.Path);
            var fingerprint = Convert.ToHexString(SHA256.HashData(input));
            if (reference.Fingerprint.Length > 0 && reference.Fingerprint != fingerprint)
            {
                throw new InvalidDataException(
                    $"External reference changed: '{reference.Path}'. Export the target again."
                );
            }

            if (captured.Metadata is null || captured.Fingerprint != fingerprint)
            {
                captured = (
                    MetadataReference.CreateFromImage(
                        _fileSystem.File.ReadAllBytes(reference.Path),
                        filePath: reference.Path
                    ),
                    fingerprint
                );
                _references[reference.Path] = captured;
            }
        }

        if (reference.Fingerprint.Length > 0 && captured.Fingerprint != reference.Fingerprint)
        {
            throw new InvalidDataException(
                $"External reference changed: '{reference.Path}'. Export the target again."
            );
        }

        return captured.Metadata.WithProperties(
            new MetadataReferenceProperties(
                (MetadataImageKind)reference.Kind,
                reference.Aliases.ToImmutableArray(),
                reference.EmbedInteropTypes
            )
        );
    }

    private static SyntaxTree CreateTree(
        ProjectSnapshot project,
        DocumentSnapshot document,
        CancellationToken cancellationToken
    )
    {
        var options = document.Options;
        var parse = new CSharpParseOptions(
            (LanguageVersion)(options?.LanguageVersion ?? project.LanguageVersion),
            (DocumentationMode)(options?.DocumentationMode ?? 1),
            (SourceCodeKind)(options?.Kind ?? 0),
            options?.PreprocessorSymbols ?? project.PreprocessorSymbols
        );
        if (options is not null)
        {
            parse = parse.WithFeatures(options.Features);
        }

        return CSharpSyntaxTree.ParseText(
            SourceText.From(document.Text, System.Text.Encoding.GetEncoding(document.EncodingName)),
            parse,
            document.Path,
            cancellationToken: cancellationToken
        );
    }

    private static CSharpCompilationOptions CreateOptions(ProjectSnapshot project)
    {
        var options = project.CompilerOptions;
        return new CSharpCompilationOptions(
            (OutputKind)project.OutputKind,
            moduleName: options.ModuleName,
            mainTypeName: options.MainTypeName,
            scriptClassName: options.ScriptClassName,
            usings: options.Usings,
            optimizationLevel: (OptimizationLevel)options.OptimizationLevel,
            checkOverflow: options.CheckOverflow,
            allowUnsafe: options.AllowUnsafe,
            cryptoPublicKey: options.CryptoPublicKey.ToImmutableArray(),
            delaySign: options.DelaySign,
            platform: (Platform)options.Platform,
            generalDiagnosticOption: (ReportDiagnostic)options.GeneralDiagnosticOption,
            warningLevel: options.WarningLevel,
            specificDiagnosticOptions: options.SpecificDiagnosticOptions.Select(
                pair => new KeyValuePair<string, ReportDiagnostic>(
                    pair.Key,
                    (ReportDiagnostic)pair.Value
                )
            ),
            deterministic: options.Deterministic,
            publicSign: options.PublicSign,
            reportSuppressedDiagnostics: options.ReportSuppressedDiagnostics,
            nullableContextOptions: (NullableContextOptions)project.NullableContextOptions,
            metadataImportOptions: (MetadataImportOptions)options.MetadataImportOptions,
            assemblyIdentityComparer: options.DesktopAssemblyIdentity
                ? DesktopAssemblyIdentityComparer.Default
                : AssemblyIdentityComparer.Default
        );
    }
}
