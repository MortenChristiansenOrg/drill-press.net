using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class CultureQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
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
                    using System.Collections.Generic;
                    using System.Globalization;
                    using System.Linq;
                    using System.Text;
                    class A { void M(string text, DateTime date, double number, int count, List<string> names, StringBuilder builder) {
                        text.StartsWith("a"); text.StartsWith('a'); text.StartsWith("a", StringComparison.Ordinal);
                        text.ToLower(); new[] { text }.Order(); new[] { 1 }.Order(); names.Sort(); names.Sort(StringComparer.Ordinal);
                        number.ToString(); count.ToString(); date.ToString("O"); date.ToString("d");
                        double.Parse("1.2"); double.Parse("1.2", CultureInfo.InvariantCulture); int.Parse("2");
                        int.Parse(text, NumberStyles.AllowThousands); int.Parse(text, NumberStyles.Integer);
                        builder.AppendFormat("{0}", number); Convert.ToDouble("1.5");
                        var formatted = $"{number} {count} {count:N0} {count:D2} {date:O} {date:d}";
                    } }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var comparisons = Code
            .Calls.Where(call => call.ComparesWithCulture())
            .In(solution)
            .Select(call => call.Operation.Syntax.ToString())
            .ToArray();
        var formats = Code
            .Calls.Where(call => call.FormatsWithCulture())
            .In(solution)
            .Select(call => call.Operation.Syntax.ToString())
            .ToArray();
        var holes = Code.Nodes<InterpolationSyntax>()
            .Where(hole => hole.FormatsWithCulture())
            .In(solution)
            .Select(hole => hole.Syntax.ToString())
            .ToArray();

        Assert.Equal(
            ["text.StartsWith(\"a\")", "text.ToLower()", "new[] { text }.Order()", "names.Sort()"],
            comparisons
        );
        Assert.Equal(
            [
                "number.ToString()",
                "date.ToString(\"d\")",
                "double.Parse(\"1.2\")",
                "int.Parse(text, NumberStyles.AllowThousands)",
                "builder.AppendFormat(\"{0}\", number)",
                "Convert.ToDouble(\"1.5\")",
            ],
            formats
        );
        Assert.Equal(["{number}", "{count:N0}", "{date:d}"], holes);
    }
}
