using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class QueryKeysTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Query_keys_read_selectors_and_direction_without_treating_constants_as_cursors()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "A",
            [
                new(
                    "A.cs",
                    """
                    using System.Linq;
                    class Item { public string Name { get; set; } = ""; public int Id { get; set; } }
                    class A { void M(IQueryable<Item> items, int last, string name) {
                        var q = items.Where(x => x.Id > last).OrderBy(x => x.Id);
                        var p = items.Where(x => x.Id > 0);
                        var r = items.Where(x => x.Name.CompareTo(name) < 0);
                    } }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var comparisons = Code
            .Calls.ToMethodsNamed("Where")
            .In(solution)
            .SelectMany(call => QueryKeys.ComparedMembers(call.Operation))
            .ToArray();
        var selectors = Code
            .Calls.ToMethodsNamed("OrderBy")
            .In(solution)
            .Select(call => QueryKeys.SelectedMemberName(call.Operation))
            .ToArray();

        Assert.Equal(new[] { ("Id", true), ("Name", false) }, comparisons);
        Assert.Equal(["Id"], selectors);
    }
}
