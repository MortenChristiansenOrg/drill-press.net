using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress.Engine;

internal static class CoverageCompilation
{
    internal static bool Matches(AnalysisProject project, MetadataReader reader)
    {
        var options = Options(reader);
        if (options is null)
            return false;
        var compilation = project.Compilation.Options;
        var symbols = options
            .GetValueOrDefault("define", "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();
        return options.GetValueOrDefault("language") == "C#"
            && options.GetValueOrDefault("output-kind") == compilation.OutputKind.ToString()
            && options.GetValueOrDefault("platform") == compilation.Platform.ToString()
            && options.GetValueOrDefault("nullable", "Disable")
                == compilation.NullableContextOptions.ToString()
            && options.GetValueOrDefault("unsafe", "False") == compilation.AllowUnsafe.ToString()
            && options.GetValueOrDefault("checked", "False") == compilation.CheckOverflow.ToString()
            && (
                options
                    .GetValueOrDefault("optimization", "debug")
                    .StartsWith("release", StringComparison.Ordinal)
                    ? OptimizationLevel.Release
                    : OptimizationLevel.Debug
            ) == compilation.OptimizationLevel
            && project.Compilation.SyntaxTrees.All(tree =>
                tree.Options is CSharpParseOptions parse
                && options.GetValueOrDefault("language-version")
                    == parse.LanguageVersion.ToDisplayString()
                && symbols.SetEquals(parse.PreprocessorSymbolNames)
                // The SDK enables interceptor namespaces by default; their implementations are in the validated source set.
                && parse.Features.Keys.All(feature => feature == "InterceptorsNamespaces")
            )
            && MatchesSources(project, reader);
    }

    private static bool MatchesSources(AnalysisProject project, MetadataReader reader)
    {
        var documents = reader.Documents.Select(reader.GetDocument).ToArray();
        return documents.Length == project.Snapshot.Documents.Length
            && project.Snapshot.Documents.All(source =>
                documents.Count(document => MatchesSource(source, reader, document)) == 1
            );
    }

    private static bool MatchesSource(
        DocumentSnapshot source,
        MetadataReader reader,
        Document document
    )
    {
        var path = reader.GetString(document.Name);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (
            !path.Equals(source.Path, comparison)
            && !(source.IsGenerated && FileName(path).Equals(FileName(source.Path), comparison))
        )
            return false;
        var algorithm = reader.GetGuid(document.HashAlgorithm);
        if (
            algorithm != new Guid("8829d00f-11b8-4213-878b-770e8597ac16")
            && algorithm != new Guid("ff1816ec-aa5e-4d10-87f7-6f4963833460")
        )
            return false;
        var bytes = SourceIdentity.Encode(source);
        var expected = reader.GetBlobBytes(document.Hash);
        return Hash(bytes, algorithm).AsSpan().SequenceEqual(expected)
            || (
                source.IsGenerated
                && Hash([.. Encoding.UTF8.GetPreamble(), .. bytes], algorithm)
                    .AsSpan()
                    .SequenceEqual(expected)
            );
    }

    private static string FileName(string path) =>
        path[(Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\')) + 1)..];

    private static byte[] Hash(byte[] bytes, Guid algorithm) =>
        algorithm == new Guid("8829d00f-11b8-4213-878b-770e8597ac16")
            ? SHA256.HashData(bytes)
            : SHA1.HashData(bytes);

    private static Dictionary<string, string>? Options(MetadataReader reader)
    {
        var records = reader
            .GetCustomDebugInformation(EntityHandle.ModuleDefinition)
            .Select(reader.GetCustomDebugInformation)
            .Where(record =>
                reader.GetGuid(record.Kind) == new Guid("b5feec05-8cd0-4a83-96da-466284bb4bd8")
            )
            .ToArray();
        if (records.Length != 1)
            return null;
        var blob = reader.GetBlobReader(records[0].Value);
        var options = new Dictionary<string, string>();
        while (blob.RemainingBytes > 0)
        {
            var key = ReadString(ref blob);
            var value = ReadString(ref blob);
            if (key is null || value is null || !options.TryAdd(key, value))
                return null;
        }
        return options;
    }

    private static string? ReadString(ref BlobReader blob)
    {
        var length = blob.IndexOf(0);
        if (length < 0)
            return null;
        var value = blob.ReadUTF8(length);
        blob.ReadByte();
        return value;
    }
}
