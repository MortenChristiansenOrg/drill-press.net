using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring;

public sealed class CodeQueryTests
{
    [Fact]
    public void Where_composes_all_conditions()
    {
        var query = Code.MemberReferences
            .Where(new RuleCondition<MemberReference>(reference => reference.MemberName.StartsWith('E')))
            .Where(new RuleCondition<MemberReference>(reference => reference.Location.FilePath == "Included.cs"));
        var references = new[]
        {
            RuleTestData.Reference<string>("Empty", "Included.cs"),
            RuleTestData.Reference<string>("Empty", "Excluded.cs"),
            RuleTestData.Reference<string>("Length", "Included.cs"),
        };

        var diagnostics = RuleTestData.Evaluate(query, references);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("Included.cs", diagnostic.Location.FilePath);
        Assert.Equal(references[0].Location, diagnostic.Location);
    }
}
