using System.IO.Abstractions;
using System.Text;
using DrillPress;
using DrillPress.Engine;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress.Testing;

/// <summary>Consumer fixtures for snippets, multiple projects, framework contexts, generated code and safe-fix agreement. Source and results remain in memory.</summary>
public sealed class RuleTestWorkspace
{
    private readonly IReadOnlyList<MetadataReference> _references;
    private readonly List<AnalysisProject> _projects = [];
    private TestCoverageFacts? _coverage;

    /// <summary>Configures synthetic, source-bound evidence for this test fixture only. Selectors bind immediately and reject missing or ambiguous occurrences; no tools or tests run.</summary>
    public RuleTestWorkspace WithCoverage(Action<TestCoverageFacts> configure)
    {
        _coverage ??= new(_projects);
        configure(_coverage);
        return this;
    }

    /// <summary>Uses the host runtime's platform assemblies for convenient snippet tests. Explicit references are required for reference-pack fidelity.</summary>
    public RuleTestWorkspace()
        : this(new FileSystem()) { }

    internal RuleTestWorkspace(IFileSystem fileSystem)
        : this(ReadReferences(fileSystem)) { }

    /// <summary>Uses consumer-supplied compiler references without reading files, supporting reference packs and fully synthetic fixtures.</summary>
    public RuleTestWorkspace(IReadOnlyList<MetadataReference> references) =>
        _references = references.ToArray();

    /// <summary>Adds an independently evaluated source project. Dependencies must already belong to this workspace. Framework labels do not select reference packs.</summary>
    /// <remarks>Defaults to nullable annotations and warnings enabled and C# 14. Source directives override the project nullable setting.</remarks>
    public AnalysisProject AddProject(
        string name,
        IReadOnlyList<TestSource> sources,
        string framework = "net10.0",
        bool isTest = false,
        IReadOnlyList<AnalysisProject>? dependencies = null,
        IReadOnlyList<string>? symbols = null,
        bool allowErrors = false,
        IReadOnlyList<MetadataReference>? references = null,
        IReadOnlyList<PackageReferenceSnapshot>? packages = null,
        NullableContextOptions nullable = NullableContextOptions.Enable,
        LanguageVersion languageVersion = LanguageVersion.CSharp14
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(framework);
        dependencies ??= [];
        symbols ??= [];
        if (dependencies.Any(dependency => !_projects.Contains(dependency)))
        {
            throw new ArgumentException(
                "Dependencies must belong to this workspace.",
                nameof(dependencies)
            );
        }

        var documents = sources
            .Select(source =>
                SourceIdentity.Capture(
                    new DocumentSnapshot(source.Path, source.Text, source.Generated),
                    Encoding.UTF8.GetBytes(source.Text),
                    "utf-8",
                    false
                )
            )
            .ToArray();
        var parse = new CSharpParseOptions(languageVersion, preprocessorSymbols: symbols);
        var compilation = CSharpCompilation.Create(
            name,
            documents.Select(document =>
                CSharpSyntaxTree.ParseText(document.Text, parse, document.Path)
            ),
            (references ?? _references).Concat(
                dependencies.Select(project => project.Compilation.ToMetadataReference())
            ),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: nullable
            )
        );
        if (!allowErrors)
        {
            var errors = compilation
                .GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();
            if (errors.Length > 0)
            {
                throw new InvalidOperationException(
                    string.Join(Environment.NewLine, errors.Select(error => error.ToString()))
                );
            }
        }

        var snapshot = new ProjectSnapshot(
            name,
            name,
            name + ".csproj",
            (int)languageVersion,
            (int)OutputKind.DynamicallyLinkedLibrary,
            (int)nullable,
            symbols.ToArray(),
            documents,
            []
        )
        {
            TargetFramework = framework,
            IsTestProject = isTest,
            Packages = packages?.ToArray() ?? [],
            ReferencedContextIds = dependencies
                .Select(project => project.Snapshot.ContextId)
                .ToArray(),
            CompilationReferences = dependencies
                .Select(project => new CompilationReferenceSnapshot(
                    project.Snapshot.ContextId,
                    [],
                    false
                ))
                .ToArray(),
        };
        var result = new AnalysisProject(snapshot, compilation);
        _projects.Add(result);
        return result;
    }

    /// <summary>Creates a fresh cache lifetime over the workspace's current projects; suitable for direct query and baseline tests.</summary>
    public AnalysisSolution Analyze(CancellationToken cancellationToken = default) =>
        new(_projects, cancellationToken);

    /// <summary>Runs the production evaluator and response validator, including cross-context safety and conflict withholding.</summary>
    public Task<RuleTestResult> CheckAsync(
        RuleSet rules,
        CancellationToken cancellationToken = default
    )
    {
        var snapshot = CompilationSnapshot.Create(
            _projects.Select(project => project.Snapshot).ToArray()
        );
        var response = new AnalysisEngine().EvaluateFixture(
            rules,
            snapshot.RequestId,
            _projects
                .Select(project => new CompilationContext(project.Snapshot, project.Compilation))
                .ToArray(),
            solution => _coverage?.Apply(solution),
            cancellationToken
        );
        return Task.FromResult(
            new RuleTestResult(snapshot, new BundleResponseValidator().Validate(snapshot, response))
        );
    }

    private static MetadataReference[] ReadReferences(IFileSystem fileSystem) =>
        (
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException(
                "No runtime assembly inventory is available; supply explicit references."
            )
        )
            .Split(fileSystem.Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromImage(fileSystem.File.ReadAllBytes(path)))
            .ToArray();
}
