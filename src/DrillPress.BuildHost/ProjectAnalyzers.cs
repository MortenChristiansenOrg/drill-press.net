using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DrillPress.BuildHost;

/// <summary>Analyzer references the target's own compiler can load, with warnings for those it rejects.</summary>
internal sealed class ProjectAnalyzers(
    string projectName,
    AnalyzerReference[] references,
    Diagnostic[] rejections
)
{
    private readonly string _projectName = projectName;
    private readonly AnalyzerReference[] _references = references;

    public Diagnostic[] Rejections { get; } = rejections;

    public T[] Load<T>(Func<AnalyzerReference, IEnumerable<T>> select)
    {
        var (loaded, failures) = TryLoad(select);
        if (failures.Length > 0)
            throw new InvalidOperationException(
                $"Analyzer loading failed in '{_projectName}': {string.Join("; ", failures)}"
            );
        return loaded;
    }

    public (T[] Loaded, string[] Failures) TryLoad<T>(
        Func<AnalyzerReference, IEnumerable<T>> select
    )
    {
        var failures = new List<string>();
        var handlers = _references
            .OfType<AnalyzerFileReference>()
            .Select(reference =>
            {
                // Matches the compiler's CS8032 wording when a named analyzer cannot be created.
                EventHandler<AnalyzerLoadFailureEventArgs> record = (_, args) =>
                    failures.Add(
                        args.ErrorCode
                        == AnalyzerLoadFailureEventArgs.FailureErrorCode.ReferencesNewerCompiler
                            ? $"Analyzer assembly '{reference.FullPath}' references compiler version '{args.ReferencedCompilerVersion}', which is newer than Drill Press's Roslyn '{typeof(AnalyzerFileReference).Assembly.GetName().Version}'. Update Drill Press."
                        : args.TypeName is null ? args.Message
                        : $"An instance of analyzer {args.TypeName} cannot be created from {reference.FullPath}: {args.Message}"
                    );
                reference.AnalyzerLoadFailed += record;
                return (Reference: reference, Record: record);
            })
            .ToArray();
        try
        {
            return (_references.SelectMany(select).ToArray(), failures.ToArray());
        }
        finally
        {
            foreach (var (reference, record) in handlers)
                reference.AnalyzerLoadFailed -= record;
        }
    }

    // Compiler warnings about analyzers are configurable like any other warning.
    public static Diagnostic CompilerWarning(string id, string message) =>
        Diagnostic.Create(
            id,
            "Compiler",
            message,
            DiagnosticSeverity.Warning,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            warningLevel: 1
        );
}
