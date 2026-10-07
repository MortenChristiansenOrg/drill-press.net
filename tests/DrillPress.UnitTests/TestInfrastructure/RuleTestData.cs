using DrillPress.Testing;
using Xunit;

namespace DrillPress.UnitTests.TestInfrastructure;

internal static class RuleTestData
{
    private const string Declarations = """
        namespace Sample
        {
            public class Target
            {
                public static Target A, Any, Empty, End, First, Length, Long, Second;
            }
        }
        """;

    /// <summary>An in-memory analysis whose documents read the listed Sample.Target members, one field initializer per line.</summary>
    public static AnalysisSolution Solution(params (string Path, string[] Members)[] documents)
    {
        var workspace = new RuleTestWorkspace([]);
        workspace.AddProject(
            "Sample",
            [
                new("Target.cs", Declarations),
                .. documents.Select(document => new TestSource(
                    document.Path,
                    Uses(document.Members)
                )),
            ],
            allowErrors: true
        );
        return workspace.Analyze(TestContext.Current.CancellationToken);
    }

    public static RuleCatalog TargetEmptyRuleCatalog()
    {
        var rules = new RuleCatalog();
        rules
            .Rule("TEST001", "Do not use Target.Empty.")
            .For(CodeType.Named("Sample.Target").Member("Empty").References)
            .Forbid();
        return rules;
    }

    public static IReadOnlyList<RuleDiagnostic> Evaluate(
        CodeQuery<MemberReference> query,
        AnalysisSolution solution
    )
    {
        var rules = new RuleCatalog();
        rules.Rule("TEST001", "Test message.").For(query).Forbid();
        return rules.Evaluate(solution);
    }

    public static (string Id, string Path, string Text) Describe(RuleDiagnostic diagnostic) =>
        (
            diagnostic.Descriptor.Id,
            diagnostic.Location.FilePath,
            diagnostic.Source!.Document.Text.Substring(
                diagnostic.Location.Start,
                diagnostic.Location.Length
            )
        );

    private static string Uses(string[] members) =>
        "class Use\n{\n"
        + string.Concat(
            members.Select(
                (member, index) => $"    Sample.Target value{index} = Sample.Target.{member};\n"
            )
        )
        + "}\n";
}
