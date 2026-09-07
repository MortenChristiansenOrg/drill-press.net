using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Security.Cryptography;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress.Engine;

internal sealed class SnapshotCompiler(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly Dictionary<string, (PortableExecutableReference Metadata, string Fingerprint)> _references = [];

    public CompilationContext[] Reconstruct(CompilationSnapshot snapshot, CancellationToken cancellationToken)
    {
        SnapshotValidation.Validate(snapshot);
        var projects = snapshot.Projects.ToDictionary(project => project.ContextId);
        var completed = new Dictionary<string, CompilationContext>();
        var visiting = new HashSet<string>();
        foreach (var project in snapshot.Projects)
        {
            Visit(project);
        }

        return snapshot.Projects.Select(project => completed[project.ContextId]).ToArray();

        CompilationContext Visit(ProjectSnapshot project)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (completed.TryGetValue(project.ContextId, out var existing))
            {
                return existing;
            }

            if (!visiting.Add(project.ContextId))
            {
                throw new InvalidDataException("The source compilation graph contains a cycle.");
            }

            var references = project.CompilationReferences.Select(reference =>
            {
                if (!projects.TryGetValue(reference.ContextId, out var dependency))
                {
                    throw new InvalidDataException("Unknown source compilation reference.");
                }

                return (MetadataReference)Visit(dependency).Compilation.ToMetadataReference(reference.Aliases.ToImmutableArray(), reference.EmbedInteropTypes);
            }).ToList();
            references.AddRange(project.ExternalReferences.Select(ReadReference));
            references.AddRange(project.MetadataReferences.Select(path => ReadReference(new MetadataReferenceSnapshot(path, "", [], false, 0))));
            references.AddRange(project.ProjectReferences.Select(reference => MetadataReference.CreateFromImage(reference.Image,
                MetadataReferenceProperties.Assembly.WithAliases(reference.Aliases).WithEmbedInteropTypes(reference.EmbedInteropTypes))));
            var trees = project.Documents.Select(document => CreateTree(project, document, cancellationToken)).ToArray();
            var options = CreateOptions(project).WithSyntaxTreeOptionsProvider(new SnapshotTreeOptions(trees.Zip(project.Documents).ToDictionary()));
            var compilation = CSharpCompilation.Create(project.AssemblyName, trees, references, options);
            var result = new CompilationContext(project, compilation);
            completed.Add(project.ContextId, result);
            visiting.Remove(project.ContextId);
            return result;
        }
    }

    private PortableExecutableReference ReadReference(MetadataReferenceSnapshot reference)
    {
        if (!_references.TryGetValue(reference.Path, out var captured))
        {
            var bytes = _fileSystem.File.ReadAllBytes(reference.Path);
            var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
            if (reference.Fingerprint.Length > 0 && reference.Fingerprint != fingerprint)
            {
                throw new InvalidDataException($"External reference changed: '{reference.Path}'. Export the target again.");
            }

            captured = (MetadataReference.CreateFromImage(bytes, filePath: reference.Path), fingerprint);
            _references.Add(reference.Path, captured);
        }

        if (reference.Fingerprint.Length > 0 && captured.Fingerprint != reference.Fingerprint)
        {
            throw new InvalidDataException($"External reference changed: '{reference.Path}'. Export the target again.");
        }

        return captured.Metadata.WithProperties(new MetadataReferenceProperties((MetadataImageKind)reference.Kind,
            reference.Aliases.ToImmutableArray(), reference.EmbedInteropTypes));
    }

    private static SyntaxTree CreateTree(ProjectSnapshot project, DocumentSnapshot document, CancellationToken cancellationToken)
    {
        var options = document.Options;
        var parse = new CSharpParseOptions((LanguageVersion)(options?.LanguageVersion ?? project.LanguageVersion),
            (DocumentationMode)(options?.DocumentationMode ?? 1), (SourceCodeKind)(options?.Kind ?? 0),
            options?.PreprocessorSymbols ?? project.PreprocessorSymbols);
        if (options is not null)
        {
            parse = parse.WithFeatures(options.Features);
        }

        return CSharpSyntaxTree.ParseText(SourceText.From(document.Text, System.Text.Encoding.GetEncoding(document.EncodingName)),
            parse, document.Path, cancellationToken: cancellationToken);
    }

    private static CSharpCompilationOptions CreateOptions(ProjectSnapshot project)
    {
        var options = project.CompilerOptions;
        return new CSharpCompilationOptions((OutputKind)project.OutputKind,
            moduleName: options.ModuleName, mainTypeName: options.MainTypeName, scriptClassName: options.ScriptClassName,
            usings: options.Usings, optimizationLevel: (OptimizationLevel)options.OptimizationLevel,
            checkOverflow: options.CheckOverflow, allowUnsafe: options.AllowUnsafe,
            cryptoPublicKey: options.CryptoPublicKey.ToImmutableArray(), delaySign: options.DelaySign,
            platform: (Platform)options.Platform, generalDiagnosticOption: (ReportDiagnostic)options.GeneralDiagnosticOption,
            warningLevel: options.WarningLevel, specificDiagnosticOptions: options.SpecificDiagnosticOptions.Select(pair =>
                new KeyValuePair<string, ReportDiagnostic>(pair.Key, (ReportDiagnostic)pair.Value)),
            deterministic: options.Deterministic, publicSign: options.PublicSign,
            reportSuppressedDiagnostics: options.ReportSuppressedDiagnostics,
            nullableContextOptions: (NullableContextOptions)project.NullableContextOptions,
            metadataImportOptions: (MetadataImportOptions)options.MetadataImportOptions,
            assemblyIdentityComparer: options.DesktopAssemblyIdentity ? DesktopAssemblyIdentityComparer.Default : AssemblyIdentityComparer.Default);
    }
}
