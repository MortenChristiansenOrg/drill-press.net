using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Queries;

public sealed class CodeQueryTests
{
    [Fact]
    public void Where_composes_all_conditions()
    {
        var query = Code
            .MemberReferences.Where(reference => reference.MemberName.StartsWith('E'))
            .Where(reference => reference.Location.FilePath == "Included.cs");
        var solution = RuleTestData.Solution(
            ("Included.cs", ["Empty", "Length"]),
            ("Excluded.cs", ["Empty"])
        );

        var diagnostics = RuleTestData.Evaluate(query, solution);

        Assert.Equal(
            [("TEST001", "Included.cs", "Sample.Target.Empty")],
            diagnostics.Select(RuleTestData.Describe)
        );
    }

    [Fact]
    public void Union_combines_selections_once_per_source_occurrence()
    {
        var empty = Code.MemberReferences.Where(reference => reference.MemberName == "Empty");
        var first = Code.MemberReferences.Where(reference => reference.Location.Line == 3);
        var solution = RuleTestData.Solution(("A.cs", ["Empty", "Any"]));

        var union = empty.Union(first).In(solution);
        var concatenation = empty.Concat(first).In(solution);

        Assert.Equal(["Empty"], union.Select(reference => reference.MemberName));
        Assert.Equal(["Empty", "Empty"], concatenation.Select(reference => reference.MemberName));
    }
}
