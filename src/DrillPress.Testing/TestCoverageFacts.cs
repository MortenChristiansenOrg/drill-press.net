using DrillPress.Manifest;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress.Testing;

/// <summary>Source-bound synthetic evidence for rule-policy tests. These fixtures do not authorize production coverage claims or emulate a collector.</summary>
public sealed class TestCoverageFacts
{
    private readonly IReadOnlyList<AnalysisProject> _projects;
    private readonly List<TestExecutionFact> _executions = [];
    private readonly List<TestLineFact> _lines = [];

    internal TestCoverageFacts(IReadOnlyList<AnalysisProject> projects) => _projects = projects;

    /// <summary>Binds an exact resolved foreach occurrence in this workspace. Advancement facts remain separate from execution facts for its collection expression.</summary>
    public TestEnumerationCoverage ForEnumeration(CodeEnumeration enumeration)
    {
        if (
            !_projects.Contains(enumeration.Source.Project)
            || !enumeration.IsResolved
            || enumeration.Source.Document.IsGenerated
            || enumeration.Syntax.SyntaxTree != enumeration.Source.Tree
        )
            throw new ArgumentException(
                "Enumeration must be a resolved occurrence in this workspace.",
                nameof(enumeration)
            );
        return new(
            new TestCallCoverage(
                this,
                enumeration.Source,
                enumeration.Syntax.Span,
                CoverageMetric.Enumeration
            )
        );
    }

    /// <summary>Binds a unique explicit call across the workspace. Repeated text or linked/framework memberships require the project overload or a bound call.</summary>
    public TestCallCoverage ForCall(string documentPath, string callText) =>
        Call(Sources(documentPath), callText);

    /// <summary>Binds a call in an exact project/framework membership. An explicit zero-based occurrence index resolves repeated text in source order.</summary>
    public TestCallCoverage ForCall(
        AnalysisProject project,
        string documentPath,
        string callText,
        int? occurrenceIndex = null
    ) => Call(Sources(documentPath, project), callText, occurrenceIndex);

    /// <summary>Binds a resolved explicit call previously selected from this workspace, retaining its exact span and compilation membership.</summary>
    public TestCallCoverage ForCall(CodeInvocation call)
    {
        if (
            !_projects.Contains(call.Source.Project)
            || !call.IsResolved
            || call.Operation.IsImplicit
            || call.Operation.Syntax.SyntaxTree != call.Source.Tree
            || call.Source.Document.IsGenerated
        )
            throw new ArgumentException(
                "Call must be a resolved explicit occurrence in this workspace.",
                nameof(call)
            );
        return new(this, call.Source, call.Operation.Syntax.Span);
    }

    /// <summary>Binds one unique source document across the workspace; linked/framework memberships require the project overload.</summary>
    public TestFileCoverage ForFile(string documentPath) => File(Sources(documentPath));

    /// <summary>Binds a document within an exact project/framework membership for file or project line thresholds.</summary>
    public TestFileCoverage ForFile(AnalysisProject project, string documentPath) =>
        File(Sources(documentPath, project));

    private AnalysisSource[] Sources(string path, AnalysisProject? project = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (project is not null && !_projects.Contains(project))
            throw new ArgumentException("Project must belong to this workspace.", nameof(project));
        return (project is null ? _projects : new[] { project })
            .SelectMany(project => project.Sources)
            .Where(source => source.Document.Path == path && !source.Document.IsGenerated)
            .ToArray();
    }

    private TestCallCoverage Call(
        AnalysisSource[] sources,
        string text,
        int? occurrenceIndex = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (occurrenceIndex is { } index)
            ArgumentOutOfRangeException.ThrowIfNegative(index);
        var calls = sources
            .SelectMany(source =>
                source
                    .Tree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Where(syntax =>
                        source
                            .Document.Text.AsSpan(syntax.Span.Start, syntax.Span.Length)
                            .SequenceEqual(text)
                    )
                    .Select(syntax =>
                        source.Model.GetOperation(syntax)
                            is Microsoft.CodeAnalysis.Operations.IInvocationOperation operation
                            ? new CodeInvocation(source, operation)
                            : null
                    )
            )
            .Where(call => call is { IsResolved: true })
            .Cast<CodeInvocation>()
            .OrderBy(call => call.Location.Start)
            .ToArray();
        if (occurrenceIndex is { } occurrence && occurrence < calls.Length)
            return ForCall(calls[occurrence]);
        if (occurrenceIndex is not null || calls.Length != 1)
            throw new ArgumentException(
                "Coverage selector must identify exactly one resolved source call; specify its project and occurrence index when ambiguous.",
                nameof(text)
            );
        return ForCall(calls[0]);
    }

    private TestFileCoverage File(AnalysisSource[] sources)
    {
        if (sources.Length != 1)
            throw new ArgumentException(
                "Coverage selector must identify exactly one source document; specify its project when ambiguous."
            );
        return new(this, sources[0]);
    }

    internal void Execution(
        AnalysisSource source,
        Microsoft.CodeAnalysis.Text.TextSpan span,
        ExecutionCoverage state,
        CoverageReason[] reasons,
        CoverageMetric metric = CoverageMetric.Execution
    )
    {
        if (
            _executions.Any(fact =>
                fact.ContextId == source.Project.Snapshot.ContextId
                && fact.Document.DocumentId == source.Document.DocumentId
                && fact.Span == span
                && fact.Metric == metric
            )
        )
            throw new InvalidOperationException(
                "This source occurrence already has a synthetic execution fact."
            );
        _executions.Add(
            new(
                source.Project.Snapshot.ContextId,
                source.Document,
                span,
                state,
                reasons.ToArray(),
                metric
            )
        );
    }

    internal void Lines(AnalysisSource source, LineCoverageMeasurement measurement)
    {
        if (
            _lines.Any(fact =>
                fact.ContextId == source.Project.Snapshot.ContextId
                && fact.Document.DocumentId == source.Document.DocumentId
            )
        )
            throw new InvalidOperationException(
                "This source document already has a synthetic line fact."
            );
        _lines.Add(new(source.Project.Snapshot.ContextId, source.Document, measurement));
    }

    internal void Apply(AnalysisSolution solution)
    {
        var contexts = solution.Projects.ToDictionary(project => project.Snapshot.ContextId);
        foreach (var fact in _executions)
        {
            var project = Context(contexts, fact.ContextId, fact.Document);
            project.Coverage.FixtureExecution(
                fact.Document.DocumentId,
                fact.Span,
                fact.State,
                fact.Reasons,
                fact.Metric
            );
        }
        foreach (var fact in _lines)
        {
            var project = Context(contexts, fact.ContextId, fact.Document);
            project.Coverage.FixtureLines(fact.Document.DocumentId, fact.Measurement);
        }
    }

    private static AnalysisProject Context(
        Dictionary<string, AnalysisProject> contexts,
        string contextId,
        DocumentSnapshot captured
    )
    {
        if (
            !contexts.TryGetValue(contextId, out var project)
            || !project.Sources.Any(source =>
                source.Document.DocumentId == captured.DocumentId
                && source.Document.Path == captured.Path
                && source.Document.Text == captured.Text
                && source.Document.Fingerprint == captured.Fingerprint
            )
        )
            throw new InvalidOperationException(
                "Synthetic coverage no longer matches its captured source membership."
            );
        return project;
    }
}
