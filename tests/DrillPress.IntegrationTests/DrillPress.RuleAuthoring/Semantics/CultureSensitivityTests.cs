using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Semantics;

public sealed class CultureSensitivityTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Comparisons_and_formats_distinguish_explicit_culture_and_invariant_shapes()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "A",
            [
                new(
                    "A.cs",
                    """
                    using System;
                    using System.Globalization;
                    using System.Linq;
                    class A { void M(string text, DateTime date, double number, int count) {
                        text.StartsWith("a"); text.StartsWith('a'); text.StartsWith("a", StringComparison.Ordinal);
                        text.ToLower(); new[] { text }.Order(); new[] { 1 }.Order();
                        number.ToString(); count.ToString(); date.ToString("O"); date.ToString("d");
                        double.Parse("1.2"); double.Parse("1.2", CultureInfo.InvariantCulture); int.Parse("2");
                        var formatted = $"{number} {count} {count:N0} {count:D2} {date:O} {date:d}";
                    } }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var comparisons = Code
            .Calls.In(solution)
            .Where(CultureSensitivity.ComparesWithCulture)
            .Select(call => call.Operation.Syntax.ToString())
            .ToArray();
        var formats = Code
            .Calls.In(solution)
            .Where(CultureSensitivity.FormatsWithCulture)
            .Select(call => call.Operation.Syntax.ToString())
            .ToArray();
        var holes = Code.Nodes<InterpolationSyntax>()
            .In(solution)
            .Where(node =>
                CultureSensitivity.FormatsWithCulture(
                    node.Syntax,
                    node.Source.Model.GetTypeInfo(node.Syntax.Expression).Type
                )
            )
            .Select(node => node.Syntax.ToString())
            .ToArray();

        Assert.Equal(
            ["text.StartsWith(\"a\")", "text.ToLower()", "new[] { text }.Order()"],
            comparisons
        );
        Assert.Equal(
            ["number.ToString()", "date.ToString(\"d\")", "double.Parse(\"1.2\")"],
            formats
        );
        Assert.Equal(["{number}", "{count:N0}", "{date:d}"], holes);
    }
}
