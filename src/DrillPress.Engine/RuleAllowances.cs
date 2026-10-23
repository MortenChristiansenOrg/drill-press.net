using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress.Engine;

/// <summary>Removes findings that source comments allow and reports allowances that are malformed or allow nothing.</summary>
internal static class RuleAllowances
{
    public const string UnusedRuleId = RuleCatalog.BuiltInPrefix + "allow-unused";
    public const string InvalidRuleId = RuleCatalog.BuiltInPrefix + "allow-invalid";

    private static readonly RuleDescriptor _unused = new(
        UnusedRuleId,
        "Remove the listed rules from this allowance; they report nothing in its scope."
    )
    {
        FixComplexity = RuleFixComplexity.Trivial,
    };

    private static readonly RuleDescriptor _invalid = new(
        InvalidRuleId,
        "Write allowances as // drillpress-allow RULE-ID[, RULE-ID]: reason, after or directly above the code."
    )
    {
        FixComplexity = RuleFixComplexity.Local,
    };

    /// <summary>Applies the allowances of every analysis-target document to the evaluated diagnostics.</summary>
    /// <param name="rules">The catalog that defines which rule IDs an allowance may name.</param>
    /// <param name="solution">The evaluated contexts.</param>
    /// <param name="diagnostics">Every finding of the evaluated rules, before fixes are planned.</param>
    /// <param name="skippedRules">Rules that were not evaluated, so allowances naming them are neither used nor unused.</param>
    public static IReadOnlyList<RuleDiagnostic> Apply(
        RuleCatalog rules,
        AnalysisSolution solution,
        IReadOnlyList<RuleDiagnostic> diagnostics,
        IReadOnlyCollection<string> skippedRules
    )
    {
        var allowances = Read(rules, solution);
        var documents = allowances
            .GroupBy(allowance =>
                (allowance.Comment.Source.Project, allowance.Comment.Source.Document.Path)
            )
            .ToDictionary(group => group.Key, group => group.ToArray());
        // A comment is used by a rule when any context reports an allowed finding, because
        // linked files and target frameworks can report a finding in only some contexts.
        var used = new HashSet<((string, int), string)>();
        var retained = new List<RuleDiagnostic>();
        foreach (var diagnostic in diagnostics)
        {
            var allowance =
                diagnostic.Source is { } source
                && documents.TryGetValue(
                    (source.Project, diagnostic.Location.FilePath),
                    out var candidates
                )
                    ? candidates
                        .Where(candidate => candidate.Allows(diagnostic))
                        .MinBy(candidate => (candidate.Scope.Length, -candidate.Scope.Start))
                    : null;
            if (allowance is null)
                retained.Add(diagnostic);
            else
                used.Add((allowance.Identity, diagnostic.Descriptor.Id));
        }

        foreach (var allowance in allowances)
        {
            if (allowance.Problem is { } problem)
            {
                retained.Add(Report(_invalid, allowance, problem));
                continue;
            }

            var unused = allowance
                .RuleIds.Distinct()
                .Where(id => !skippedRules.Contains(id) && !used.Contains((allowance.Identity, id)))
                .ToArray();
            if (unused.Length == 0)
                continue;
            // Build configurations that were not loaded may report findings in conditional code, so
            // the allowance is reported for review but never removed automatically.
            var evidence = string.Join(", ", unused);
            retained.Add(
                allowance.IsConditional
                    ? Report(
                        _unused,
                        allowance,
                        evidence + "; another build configuration may use it"
                    )
                    : Report(_unused, allowance, evidence) with
                    {
                        Fix = allowance.Without(unused),
                    }
            );
        }

        return retained
            .OrderBy(diagnostic => diagnostic.Descriptor.Id, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.FilePath, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.Start)
            .ToArray();
    }

    private static List<RuleAllowance> Read(RuleCatalog rules, AnalysisSolution solution)
    {
        var allowances = new List<RuleAllowance>();
        foreach (
            var source in solution
                .Projects.Where(project => project.Snapshot.IsAnalysisTarget)
                .SelectMany(project => project.Sources)
                .Where(source =>
                    !source.Document.IsGenerated
                    && source.Document.Text.Contains(RuleAllowance.Marker, StringComparison.Ordinal)
                )
        )
        {
            foreach (
                var trivia in source
                    .Tree.GetRoot(solution.CancellationToken)
                    .DescendantTrivia()
                    .Where(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
            )
            {
                if (RuleAllowance.Read(new CodeComment(source, trivia), rules) is { } allowance)
                    allowances.Add(allowance);
            }
        }

        return allowances;
    }

    private static RuleDiagnostic Report(
        RuleDescriptor descriptor,
        RuleAllowance allowance,
        string evidence
    ) =>
        new(descriptor, allowance.Comment.Location)
        {
            Source = allowance.Comment.Source,
            Evidence = evidence,
        };
}
