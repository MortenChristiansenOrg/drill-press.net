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
    private readonly Lazy<(DiagnosticSuppressor[] Loaded, string[] Failures)> _suppressors = new(
        () =>
            TryLoad(
                references,
                reference =>
                    reference.GetAnalyzers(LanguageNames.CSharp).OfType<DiagnosticSuppressor>()
            )
    );

    public Diagnostic[] Rejections { get; } = rejections;

    /// <summary>The project's diagnostic suppressors, loaded once, with the messages of those that failed to load.</summary>
    public (DiagnosticSuppressor[] Loaded, string[] Failures) Suppressors => _suppressors.Value;

    /// <summary>The diagnostic IDs the loaded suppressors declare they can suppress, in ordinal order.</summary>
    public string[] SuppressibleDiagnosticIds() =>
        Suppressors
            .Loaded.SelectMany(SupportedIds)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();

    public T[] Load<T>(Func<AnalyzerReference, IEnumerable<T>> select)
    {
        var (loaded, failures) = TryLoad(_references, select);
        if (failures.Length > 0)
            throw new InvalidOperationException(
                $"Analyzer loading failed in '{_projectName}': {string.Join("; ", failures)}"
            );
        return loaded;
    }

    private static (T[] Loaded, string[] Failures) TryLoad<T>(
        AnalyzerReference[] references,
        Func<AnalyzerReference, IEnumerable<T>> select
    )
    {
        var failures = new List<string>();
        var handlers = references
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
            return (references.SelectMany(select).ToArray(), failures.ToArray());
        }
        finally
        {
            foreach (var (reference, record) in handlers)
                reference.AnalyzerLoadFailed -= record;
        }
    }

    // The compiler runs no suppressions for a suppressor whose descriptors throw; it reports AD0001 instead.
    private static IEnumerable<string> SupportedIds(DiagnosticSuppressor suppressor)
    {
        try
        {
            return suppressor
                .SupportedSuppressions.Select(descriptor => descriptor.SuppressedDiagnosticId)
                .ToArray();
        }
        catch (Exception)
        {
            return [];
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
