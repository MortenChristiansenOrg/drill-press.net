using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;
using DrillPress.Manifest;

namespace DrillPress.Benchmarks;

public sealed class SignatureNormalization
{
    private readonly IFileSystem _fileSystem;
    private readonly string _root;
    private readonly Dictionary<string, string> _contexts;
    private readonly Dictionary<string, string> _documents;
    private readonly Dictionary<string, string> _files;
    private readonly Dictionary<string, string> _paths;
    private readonly Dictionary<string, string> _batches = [];

    public SignatureNormalization(IFileSystem fileSystem, CompilationSnapshot snapshot, string root)
    {
        _fileSystem = fileSystem;
        _root = fileSystem.Path.GetFullPath(root);
        _contexts = snapshot.Projects.ToDictionary(project => project.ContextId, project => Hash(new
        {
            Path = Path(project.ProjectPath), project.TargetFramework,
            Properties = project.Properties.OrderBy(pair => pair.Key).ToArray(),
        }));
        _paths = snapshot.Projects.SelectMany(project => project.Documents.Select(document =>
            (document.Path, Normalized: DocumentPath(project, document)))).Distinct()
            .ToDictionary(item => item.Path, item => item.Normalized);
        _documents = snapshot.Projects.SelectMany(project => project.Documents.Select(document =>
            (document.DocumentId, Identity: Hash(new { Context = _contexts[project.ContextId], Path = _paths[document.Path] }))))
            .ToDictionary(item => item.DocumentId, item => item.Identity);
        if (_contexts.Values.Distinct().Count() != _contexts.Count || _documents.Values.Distinct().Count() != _documents.Count)
        {
            throw new InvalidDataException("Benchmark contexts or document memberships do not have unique normalized identities.");
        }

        _files = snapshot.Projects.SelectMany(project => project.Documents).GroupBy(document => document.FileIdentity)
            .ToDictionary(group => group.Key, group => _paths[group.First().Path]);
    }

    public CompilationSnapshot Snapshot(CompilationSnapshot snapshot) => snapshot with
    {
        RequestId = "request",
        Projects = snapshot.Projects.Select(project => project with
        {
            ContextId = _contexts[project.ContextId], ProjectPath = Path(project.ProjectPath),
            Properties = Sort(project.Properties),
            CompilerOptions = project.CompilerOptions with { SpecificDiagnosticOptions = Sort(project.CompilerOptions.SpecificDiagnosticOptions) },
            ReferencedContextIds = project.ReferencedContextIds.Select(id => _contexts[id]).ToArray(),
            CompilationReferences = project.CompilationReferences.Select(reference => reference with { ContextId = _contexts[reference.ContextId] }).ToArray(),
            MetadataReferences = project.MetadataReferences.Select(Path).ToArray(),
            ExternalReferences = project.ExternalReferences.Select(reference => reference with { Path = Path(reference.Path) }).ToArray(),
            Documents = project.Documents.Select(document => document with
            {
                DocumentId = _documents[document.DocumentId], FileIdentity = _files[document.FileIdentity], Path = _paths[document.Path],
                Options = document.Options is null ? null : document.Options with
                {
                    Features = Sort(document.Options.Features), DiagnosticOptions = Sort(document.Options.DiagnosticOptions),
                },
            }).ToArray(),
        }).ToArray(),
    };

    public BundleResponse Response(BundleResponse response)
    {
        var batches = response.Batches.Select(Batch).ToArray();
        return response with
        {
            RequestId = "request", Batches = batches,
            Contexts = response.Contexts.Select(context => context with
            {
                ContextId = _contexts[context.ContextId],
                Findings = context.Findings.Select(finding => finding with
                {
                    DocumentId = _documents[finding.DocumentId], BatchId = BatchId(finding.BatchId),
                }).ToArray(),
            }).ToArray(),
        };
    }

    public ValidatedResult Plan(ValidatedResult plan) => plan with
    {
        Findings = plan.Findings.Select(finding => finding with
        {
            FileIdentity = _files[finding.FileIdentity], Path = Path(finding.Path), BatchId = BatchId(finding.BatchId),
        }).ToArray(),
        Batches = plan.Batches.Select(Batch).OrderBy(batch => batch.Id).ToArray(), Edits = plan.Edits.Select(Edit).ToArray(),
    };

    public static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private FixBatch Batch(FixBatch batch)
    {
        var edits = batch.Edits.Select(Edit).ToArray();
        var validations = batch.Validations.Select(validation => validation with { ContextId = _contexts[validation.ContextId] }).ToArray();
        var id = Hash(new { Edits = edits, Validations = validations });
        _batches[batch.Id] = id;
        return new(id, edits, validations);
    }

    private SourceEdit Edit(SourceEdit edit) => edit with { FileIdentity = _files[edit.FileIdentity] };
    private string? BatchId(string? id) => id is null ? null : _batches[id];

    private static Dictionary<string, TValue> Sort<TValue>(Dictionary<string, TValue> values) =>
        values.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value);

    private string DocumentPath(ProjectSnapshot project, DocumentSnapshot document)
    {
        var prefix = "drillpress-generated://" + project.ContextId + "/";
        return document.IsGenerated && document.Path.StartsWith(prefix, StringComparison.Ordinal)
            ? "drillpress-generated://" + _contexts[project.ContextId] + "/" + document.Path[prefix.Length..]
            : Path(document.Path);
    }

    private string Path(string path)
    {
        if (!_fileSystem.Path.IsPathRooted(path))
        {
            return path;
        }

        var relative = _fileSystem.Path.GetRelativePath(_root, path);
        return relative == ".." || relative.StartsWith(".." + _fileSystem.Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            _fileSystem.Path.IsPathRooted(relative) ? path : _fileSystem.Path.Combine(_fileSystem.Directory.GetCurrentDirectory(), "__workload__", relative);
    }
}
