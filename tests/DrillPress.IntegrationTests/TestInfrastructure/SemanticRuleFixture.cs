using System.IO.Abstractions;
using System.Text;
using DrillPress.Engine;
using DrillPress.Manifest;
using DrillPress.SampleRules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress.IntegrationTests.TestInfrastructure;

public sealed class SemanticRuleFixture
{
    private readonly MetadataReference[] _references;

    public SemanticRuleFixture()
    {
        IFileSystem fileSystem = new FileSystem();
        string[] names = ["netstandard", "System.Private.CoreLib", "System.Runtime", "System.Collections", "System.Linq", "System.Linq.Expressions",
            "System.Threading", "System.Threading.Tasks", "xunit.v3.core", "xunit.v3.assert", "xunit.v3.common"];
        _references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(fileSystem.Path.PathSeparator)
            .Where(path => names.Contains(fileSystem.Path.GetFileNameWithoutExtension(path)))
            .Select(path => MetadataReference.CreateFromImage(fileSystem.File.ReadAllBytes(path))).ToArray();
    }

    public AnalysisProject Project(string source, string name = "Test", bool isTest = false, string framework = "net10.0",
        string[]? symbols = null, AnalysisProject[]? dependencies = null, DocumentSnapshot[]? extra = null, bool allowErrors = false)
    {
        dependencies ??= [];
        var document = SourceIdentity.Capture(new DocumentSnapshot(name + ".cs", source, false), Encoding.UTF8.GetBytes(source), "utf-8", false);
        var documents = new[] { document }.Concat(extra ?? []).ToArray();
        var options = new CSharpParseOptions(LanguageVersion.CSharp14, preprocessorSymbols: symbols ?? []);
        var compilation = CSharpCompilation.Create(name, documents.Select(item => CSharpSyntaxTree.ParseText(item.Text, options, item.Path)),
            _references.Concat(dependencies.Select(dependency => dependency.Compilation.ToMetadataReference())),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (!allowErrors && errors.Length != 0)
        {
            throw new InvalidOperationException(string.Join("\n", errors.Select(error => error.ToString())));
        }

        var snapshot = new ProjectSnapshot(name, name, name + ".csproj", (int)LanguageVersion.CSharp14,
            (int)OutputKind.DynamicallyLinkedLibrary, (int)NullableContextOptions.Enable, symbols ?? [], documents, [])
        {
            ContextId = name + "-" + framework,
            TargetFramework = framework,
            IsTestProject = isTest,
            ReferencedContextIds = dependencies.Select(dependency => dependency.Snapshot.ContextId).ToArray(),
        };
        return new(snapshot, compilation);
    }

    public Task<BundleResponse> Evaluate(params AnalysisProject[] projects) => new AnalysisEngine().EvaluateAsync(
        SampleRuleSet.Create(), "test-request", projects.Select(project => new CompilationContext(project.Snapshot, project.Compilation)).ToArray(),
        Xunit.TestContext.Current.CancellationToken);

    public async Task<(string Rule, int Line, string Text, string? Replacement)[]> Describe(params AnalysisProject[] projects)
    {
        var response = await Evaluate(projects);
        return response.Contexts.SelectMany(context => context.Findings.Select(finding =>
        {
            var project = projects.Single(project => project.Snapshot.ContextId == context.ContextId);
            var source = project.Sources.Single(source => source.Document.DocumentId == finding.DocumentId);
            return (finding.RuleId, source.Locate(new(finding.Start, finding.Length)).Line,
                source.Document.Text.Substring(finding.Start, finding.Length),
                response.Batches.SingleOrDefault(batch => batch.Id == finding.BatchId)?.Edits.Single().Replacement);
        })).ToArray();
    }
}
