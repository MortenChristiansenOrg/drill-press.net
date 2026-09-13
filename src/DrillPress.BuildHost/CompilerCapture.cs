using System.IO.Abstractions;
using System.Security.Cryptography;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress.BuildHost;

internal sealed class CompilerCapture(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    public ProjectSnapshot Capture(
        string name,
        string projectPath,
        string contextId,
        CSharpCompilation compilation,
        HashSet<SyntaxTree> generated,
        IEnumerable<string> diagnosticIds,
        CancellationToken cancellationToken
    )
    {
        var parse = (CSharpParseOptions)(
            compilation.SyntaxTrees.FirstOrDefault()?.Options ?? CSharpParseOptions.Default
        );
        var options = compilation.Options;
        return new ProjectSnapshot(
            name,
            compilation.AssemblyName!,
            projectPath,
            (int)parse.LanguageVersion,
            (int)options.OutputKind,
            (int)options.NullableContextOptions,
            parse.PreprocessorSymbolNames.ToArray(),
            compilation
                .SyntaxTrees.Select(
                    (tree, index) =>
                        CaptureDocument(
                            tree,
                            contextId,
                            index,
                            generated.Contains(tree),
                            options.SyntaxTreeOptionsProvider,
                            diagnosticIds,
                            cancellationToken
                        )
                )
                .ToArray(),
            []
        )
        {
            ContextId = contextId,
            ExternalReferences = compilation
                .References.OfType<PortableExecutableReference>()
                .Select(CaptureReference)
                .ToArray(),
            CompilerOptions = new CompilerOptionsSnapshot
            {
                MetadataImportOptions = (int)options.MetadataImportOptions,
                DesktopAssemblyIdentity =
                    options.AssemblyIdentityComparer is DesktopAssemblyIdentityComparer,
                OptimizationLevel = (int)options.OptimizationLevel,
                Platform = (int)options.Platform,
                AllowUnsafe = options.AllowUnsafe,
                CheckOverflow = options.CheckOverflow,
                WarningLevel = options.WarningLevel,
                GeneralDiagnosticOption = (int)options.GeneralDiagnosticOption,
                SpecificDiagnosticOptions = options.SpecificDiagnosticOptions.ToDictionary(
                    pair => pair.Key,
                    pair => (int)pair.Value
                ),
                ReportSuppressedDiagnostics = options.ReportSuppressedDiagnostics,
                Deterministic = options.Deterministic,
                MainTypeName = options.MainTypeName,
                ModuleName = options.ModuleName,
                ScriptClassName = options.ScriptClassName,
                Usings = options.Usings.ToArray(),
                CryptoPublicKey = compilation.Assembly.Identity.PublicKey.ToArray(),
                PublicSign = options.PublicSign,
                DelaySign = options.DelaySign,
            },
        };
    }

    private MetadataReferenceSnapshot CaptureReference(PortableExecutableReference reference)
    {
        var path =
            reference.FilePath
            ?? throw new InvalidDataException("Metadata reference has no persistent input path.");
        path = _fileSystem.Path.GetFullPath(path);
        var bytes = _fileSystem.File.ReadAllBytes(path);
        return new MetadataReferenceSnapshot(
            path,
            Convert.ToHexString(SHA256.HashData(bytes)),
            reference.Properties.Aliases.ToArray(),
            reference.Properties.EmbedInteropTypes,
            (int)reference.Properties.Kind
        );
    }

    private DocumentSnapshot CaptureDocument(
        SyntaxTree tree,
        string contextId,
        int index,
        bool generated,
        SyntaxTreeOptionsProvider? provider,
        IEnumerable<string> diagnosticIds,
        CancellationToken cancellationToken
    )
    {
        var text = tree.GetText(cancellationToken);
        var path = string.IsNullOrWhiteSpace(tree.FilePath)
            ? $"drillpress-generated://{contextId}/{index:D6}.g.cs"
            : _fileSystem.Path.GetFullPath(tree.FilePath);
        var parse = (CSharpParseOptions)tree.Options;
        var diagnostics = new Dictionary<string, int>();
        foreach (var id in diagnosticIds)
        {
            if (
                provider is not null
                && (
                    provider.TryGetDiagnosticValue(tree, id, cancellationToken, out var severity)
                    || provider.TryGetGlobalDiagnosticValue(id, cancellationToken, out severity)
                )
            )
            {
                diagnostics[id] = (int)severity;
            }
        }

        generated |=
            provider?.IsGenerated(tree, cancellationToken) == GeneratedKind.MarkedGenerated
            || path.Replace('\\', '/').Split('/').Contains("obj", StringComparer.OrdinalIgnoreCase)
            || path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
            || text.ToString()
                .AsSpan(0, Math.Min(text.Length, 2048))
                .Contains("<auto-generated", StringComparison.OrdinalIgnoreCase);
        if (generated && !_fileSystem.File.Exists(path))
        {
            path =
                $"drillpress-generated://{contextId}/{index:D6}/{_fileSystem.Path.GetFileName(path)}";
        }

        var document = new DocumentSnapshot(path, text.ToString(), generated)
        {
            FileIdentity = path,
            Options = new SourceOptionsSnapshot
            {
                LanguageVersion = (int)parse.LanguageVersion,
                DocumentationMode = (int)parse.DocumentationMode,
                Kind = (int)parse.Kind,
                PreprocessorSymbols = parse.PreprocessorSymbolNames.ToArray(),
                Features = parse.Features.ToDictionary(),
                DiagnosticOptions = diagnostics,
            },
        };
        if (!generated)
        {
            var bytes = _fileSystem.File.ReadAllBytes(path);
            var encoding = text.Encoding ?? System.Text.Encoding.UTF8;
            var preamble = encoding.GetPreamble();
            document = SourceIdentity.Capture(
                document,
                bytes,
                encoding.WebName,
                preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble)
            );
            if (HasSymbolicAlias(path))
            {
                document = document with { IsEditable = false };
            }
        }

        return document;
    }

    private bool HasSymbolicAlias(string path)
    {
        if (_fileSystem.FileInfo.New(path).LinkTarget is not null)
        {
            return true;
        }

        var directory = _fileSystem.DirectoryInfo.New(_fileSystem.Path.GetDirectoryName(path)!);
        while (directory is not null)
        {
            if (directory.LinkTarget is not null)
            {
                return true;
            }

            directory = directory.Parent;
        }

        return false;
    }
}
