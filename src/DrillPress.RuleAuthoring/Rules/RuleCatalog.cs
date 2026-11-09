namespace DrillPress;

/// <summary>Collects rule definitions. Every rule starts with <see cref="Rule(string, string, RuleFixComplexity?)"/>, followed by one or more typed clauses.</summary>
/// <example>
/// <code>
/// var rules = new RuleCatalog();
/// rules.Rule("TEAM001", "Use the application logger instead of Console.WriteLine.")
///     .For(Code.Calls.To(CodeType.Named("System.Console").Member("WriteLine")))
///     .Forbid();
/// </code>
/// </example>
public sealed class RuleCatalog
{
    // Built-in engine rules, such as allowance checks, use this prefix so they cannot clash with bundle rules.
    internal const string BuiltInPrefix = "drillpress-";
    internal const string AllowUnusedId = BuiltInPrefix + "allow-unused";
    internal const string AllowInvalidId = BuiltInPrefix + "allow-invalid";
    private const int MaxListedIds = 20;

    private readonly List<RuleRegistration> _rules;
    private readonly IReadOnlySet<string>? _selected;
    private readonly IReadOnlySet<string> _ignored;
    private ReportingScope _reporting;

    /// <summary>Creates an empty catalog that reports in the whole analysis target.</summary>
    public RuleCatalog()
        : this([], null, new HashSet<string>(), ReportingScope.Targets) { }

    private RuleCatalog(
        List<RuleRegistration> rules,
        IReadOnlySet<string>? selected,
        IReadOnlySet<string> ignored,
        ReportingScope reporting
    )
    {
        _rules = rules;
        _selected = selected;
        _ignored = ignored;
        _reporting = reporting;
    }

    /// <summary>Where this catalog's findings, fixes and allowance diagnostics may land.</summary>
    internal ReportingScope Reporting => _reporting;

    // Selection leaves every rule defined, so allowances naming an unselected rule stay valid.
    internal bool Defines(string id) => _rules.Any(rule => rule.Id == id);

    /// <summary>Whether findings of this rule, including a built-in allowance rule, are evaluated and reported.</summary>
    internal bool Selects(string id) =>
        (_selected is null || _selected.Contains(id)) && !_ignored.Contains(id);

    private IEnumerable<RuleRegistration> SelectedRules => _rules.Where(rule => Selects(rule.Id));

    internal bool RequiresCoverage => SelectedRules.Any(rule => rule.RequiresCoverage);

    internal string[] SkippedRuleIds(AnalysisOptions options) =>
        options.SkipCoverage
            ? SelectedRules
                .Where(rule => rule.RequiresCoverage)
                .Select(rule => rule.Id)
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];

    internal IReadOnlySet<AnalysisProject> CoverageContexts(AnalysisSolution solution)
    {
        var planning = new AnalysisSolution(
            solution.Projects,
            solution.Options,
            solution.CancellationToken,
            false
        )
        {
            IsPlanningCoverage = true,
        };
        return SelectedRules
            .SelectMany(rule => rule.CoverageContexts(planning, _reporting))
            .Where(_reporting.Includes)
            .ToHashSet();
    }

    /// <summary>
    /// Stops every rule from reporting in projects whose name matches a case-sensitive glob, such as
    /// <c>Contoso.ApiEmulator</c> or <c>Contoso.ApiEmulator.*</c>, with the pattern rules of
    /// <see cref="ScopeQueries.InProject{T}(CodeQuery{T}, string)"/>. Excluded projects still load and
    /// contribute compilations and semantic facts, such as implementations, references and fix validation,
    /// but receive no findings, fixes or allowance diagnostics. A linked file still reports for the projects
    /// that are not excluded. Repeated calls add patterns.
    /// </summary>
    /// <param name="patterns">Project name globs.</param>
    /// <exception cref="ArgumentException">A pattern is blank.</exception>
    public void ExcludeProjects(params string[] patterns) =>
        _reporting = _reporting.ExcludingProjects(patterns);

    /// <summary>
    /// Returns a catalog that shares these rule definitions but evaluates and reports only the selected rules, and
    /// reports nothing in source files whose full path matches an excluded glob. This catalog is unchanged.
    /// </summary>
    /// <exception cref="ArgumentException">The selection names a rule this catalog does not define.</exception>
    internal RuleCatalog Restrict(RuleSelection selection)
    {
        if (
            selection.Select is null
            && selection.Ignore.Count == 0
            && selection.ExcludedPaths.Count == 0
        )
            return this;
        string[] builtIns = [AllowInvalidId, AllowUnusedId];
        var known = _rules.Select(rule => rule.Id).Concat(builtIns).ToHashSet();
        var unknown = (selection.Select ?? [])
            .Concat(selection.Ignore)
            .Where(id => !known.Contains(id))
            .Distinct()
            .ToArray();
        // Short catalogs list their IDs; long lists would cost more context than they save.
        if (unknown.Length > 0)
            throw new ArgumentException(
                $"Rule selection names unknown rule IDs: {string.Join(", ", unknown)}."
                    + (
                        known.Count <= MaxListedIds
                            ? $" Known IDs: {string.Join(", ", known.Order(StringComparer.Ordinal))}."
                            : $" The bundle defines {known.Count} rule IDs."
                    )
            );
        return new(
            [.. _rules],
            selection.Select?.ToHashSet(),
            selection.Ignore.ToHashSet(),
            _reporting.ExcludingPaths(selection.ExcludedPaths)
        );
    }

    /// <summary>Reserves a unique rule identity with its single-line remediation message and optional typical agent fix effort. Add clauses with <see cref="RuleDefinition.For{T}(CodeQuery{T})"/>.</summary>
    /// <param name="id">The stable, unique rule identifier shown in output.</param>
    /// <param name="message">What to do instead, shown once per rule.</param>
    /// <param name="fixComplexity">Optional estimate of the effort an agent needs for a typical fix.</param>
    public RuleDefinition Rule(
        string id,
        string message,
        RuleFixComplexity? fixComplexity = null
    ) => Rule(new RuleDescriptor(id, message) { FixComplexity = fixComplexity });

    /// <summary>Reserves a configured descriptor's identity. No other definition can reuse its ID.</summary>
    /// <exception cref="ArgumentException">The ID or message is blank or multiline, or the ID starts with the built-in <c>drillpress-</c> prefix.</exception>
    public RuleDefinition Rule(RuleDescriptor descriptor)
    {
        ValidateDescriptor(descriptor);
        if (_rules.Any(rule => rule.Descriptor.Id == descriptor.Id))
            throw new InvalidOperationException(
                $"Rule id '{descriptor.Id}' is registered more than once."
            );
        var registration = new RuleRegistration(descriptor);
        _rules.Add(registration);
        return new(registration);
    }

    /// <summary>Evaluates every registered rule over one shared source graph and returns diagnostics in deterministic order. <see cref="AnalysisOptions.SkipCoverage"/> omits coverage rules, and <see cref="ExcludeProjects"/> omits findings in excluded projects.</summary>
    /// <exception cref="InvalidOperationException">A rule has no <c>Forbid</c> or <c>Require</c> clause.</exception>
    public IReadOnlyList<RuleDiagnostic> Evaluate(AnalysisSolution solution)
    {
        solution.CancellationToken.ThrowIfCancellationRequested();
        if (_rules.FirstOrDefault(rule => rule.IsEmpty) is { } empty)
            throw new InvalidOperationException(
                $"Rule '{empty.Descriptor.Id}' has no clause; end each For(...) with Forbid(...) or Require(...)."
            );
        var diagnostics = new List<RuleDiagnostic>();
        ArgumentOutOfRangeException.ThrowIfLessThan(
            solution.Options.QueryPreparationConcurrency,
            1
        );
        if (solution.Options.QueryPreparationConcurrency > 1)
        {
            using var preparation = solution.Options.Profile.Measure("queries.preparation");
            Parallel.ForEach(
                SelectedRules.Where(rule =>
                    !(solution.Options.SkipCoverage && rule.RequiresCoverage)
                ),
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = solution.Options.QueryPreparationConcurrency,
                    CancellationToken = solution.CancellationToken,
                },
                rule =>
                {
                    using var measurement = solution.Options.Profile.Measure("query." + rule.Id);
                    try
                    {
                        rule.Prepare(solution);
                    }
                    catch (Exception)
                    {
                        // Lazy query failures surface again during ordered rule evaluation.
                    }
                }
            );
        }
        foreach (
            var rule in SelectedRules.Where(rule =>
                !(solution.Options.SkipCoverage && rule.RequiresCoverage)
            )
        )
        {
            using var measurement = solution.Options.Profile.Measure(
                "rule." + rule.Id,
                "rule." + rule.Id + ".own"
            );
            diagnostics.AddRange(rule.Evaluate(solution, _reporting));
        }

        solution.Options.Profile.WriteShared();
        solution.WriteProfileCounters();
        return diagnostics
            .OrderBy(diagnostic => diagnostic.Descriptor.Id, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.FilePath, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.Start)
            .ToArray();
    }

    private static void ValidateDescriptor(RuleDescriptor descriptor)
    {
        var (id, message) = descriptor;
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (descriptor.FixComplexity is { } complexity && !Enum.IsDefined(complexity))
            throw new ArgumentOutOfRangeException(
                nameof(descriptor),
                "Unknown rule fix complexity."
            );
        if (
            id.Any(char.IsControl)
            || message.Any(char.IsControl)
            || id.Contains('\u2028')
            || id.Contains('\u2029')
            || message.Contains('\u2028')
            || message.Contains('\u2029')
        )
        {
            throw new ArgumentException(
                "Rule identifiers and remediation messages must be single-line."
            );
        }
        if (id.StartsWith(BuiltInPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Rule identifiers starting with '{BuiltInPrefix}' are reserved for built-in rules.",
                nameof(descriptor)
            );
    }
}
