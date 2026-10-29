using System.Text.RegularExpressions;
using DrillPress.Testing;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Semantics;

public sealed class SymbolQueriesTests
{
    private const string Source = """
        using System;
        using ØAlias = System.String;
        namespace Sample;
        interface IØ { void Read(); }
        class Plain : IØ
        {
            public Plain() { }
            int æField;
            public string ØProperty { get; set; } = "";
            void IØ.Read() { }
            void Work(int åParameter)
            {
                int øLocal = 1;
                void ØNested() { }
                foreach (var æItem in new[] { 1 }) { }
                var anonymous = new { åName = 1 };
                var tuple = (æTuple: 1, Other: 2);
                try { } catch (Exception øError) { }
            }
        }
        """;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Regex_names_retain_qualified_and_synthesized_declaration_names(bool optimize)
    {
        var workspace = new RuleTestWorkspace();
        workspace.AddProject("Sample", [new("Names.cs", Source)]);
        var initial = workspace.Analyze(TestContext.Current.CancellationToken);
        var solution = new AnalysisSolution(
            initial.Projects,
            new AnalysisOptions { EnableOptimizations = optimize },
            TestContext.Current.CancellationToken
        );
        var query = Code.Declarations.NameMatching(new Regex("[æøåÆØÅ]"));

        var declarations = query.In(solution);

        Assert.Equal(
            [
                ("Alias", "ØAlias"),
                ("NamedType", "IØ"),
                ("Field", "æField"),
                ("Property", "ØProperty"),
                ("Method", "get_ØProperty"),
                ("Method", "set_ØProperty"),
                ("Method", "Sample.IØ.Read"),
                ("Parameter", "åParameter"),
                ("Local", "øLocal"),
                ("Method", "ØNested"),
                ("Local", "æItem"),
                ("Property", "åName"),
                ("Field", "æTuple"),
                ("Local", "øError"),
            ],
            declarations.Select(declaration =>
                (declaration.Symbol.Kind.ToString(), declaration.Name)
            )
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Exact_names_do_not_confuse_written_and_qualified_or_constructor_names(bool optimize)
    {
        var workspace = new RuleTestWorkspace();
        workspace.AddProject("Sample", [new("Names.cs", Source)]);
        var initial = workspace.Analyze(TestContext.Current.CancellationToken);
        var solution = new AnalysisSolution(
            initial.Projects,
            new AnalysisOptions { EnableOptimizations = optimize },
            TestContext.Current.CancellationToken
        );
        var query = Code.Declarations.Named(".ctor", "Sample.IØ.Read", "ØNested");

        var declarations = query.In(solution);

        Assert.Equal(
            [".ctor", "Sample.IØ.Read", "ØNested"],
            declarations.Select(declaration => declaration.Name)
        );
    }
}
