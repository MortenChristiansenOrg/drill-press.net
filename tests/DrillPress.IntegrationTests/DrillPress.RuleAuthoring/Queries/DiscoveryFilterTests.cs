using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class DiscoveryFilterTests(SemanticRuleFixture fixture)
    : IClassFixture<SemanticRuleFixture>
{
    [Fact]
    public void Place_filters_bind_only_documents_in_the_selected_places()
    {
        var production = fixture.Project(
            "class P { string V => string.Empty; void M() => M(); P Make() => new P(); }",
            "Production"
        );
        var tests = fixture.Project(
            "class T { string V => string.Empty; void M() => M(); }",
            "Tests",
            isTest: true
        );
        var rules = new RuleCatalog();
        rules
            .Rule("A", "References.")
            .For(
                Code.MemberReferences.InTestProjects()
                    .Where(reference => reference.MemberName == "Empty")
            )
            .Forbid();
        rules
            .Rule("B", "Calls.")
            .For(Code.Calls.Where(call => call.Target.Name == "M").InTestProjects())
            .Forbid();
        rules
            .Rule("C", "Types.")
            .For(
                Code.TypeReferences.InNonTestProjects()
                    .Where(reference => reference.Type.Name == "P")
            )
            .Forbid();
        rules
            .Rule("D", "Creations.")
            .For(
                Code.ObjectCreations.InFilesNamed("Production.cs")
                    .Where(creation => creation.IsResolved)
            )
            .Forbid();
        using var writer = new StringWriter();
        var optimized = new AnalysisSolution(
            [production, tests],
            new AnalysisOptions { Profile = new PipelineProfile(true, writer, "rules") }
        );
        var exhaustive = new AnalysisSolution(
            [production, tests],
            new AnalysisOptions { EnableOptimizations = false }
        );

        var diagnostics = rules.Evaluate(optimized);

        Assert.Equal(rules.Evaluate(exhaustive).Select(Describe), diagnostics.Select(Describe));
        Assert.Equal(
            ["A Tests.cs", "B Tests.cs", "C Production.cs", "C Production.cs", "D Production.cs"],
            diagnostics.Select(diagnostic =>
                diagnostic.Descriptor.Id + " " + diagnostic.Location.FilePath
            )
        );
        Assert.Equal(2, Counter(writer.ToString(), "member.symbol.bindings"));
        Assert.Equal(1, Counter(writer.ToString(), "operation.bound.documents"));
    }

    [Fact]
    public void Creations_of_a_type_include_aliases_qualified_names_and_target_typed_new()
    {
        var project = fixture.Project(
            """
            using Alias = Shop.Widget;
            namespace Shop
            {
                class Widget { }
                class Box<T> { }
                class C
                {
                    object[] M() => [new Widget(), new Shop.Widget(), new Alias(), new global::Shop.Widget(), new Box<int>()];
                    Widget N() => new();
                    object O() => new object();
                }
            }
            """
        );
        var widgets = Code.ObjectCreations.Of(CodeType.Named("Shop.Widget"));
        var boxes = Code.ObjectCreations.Of(CodeType.Named("Shop.Box<>"));
        var optimized = new AnalysisSolution([project]);
        var exhaustive = new AnalysisSolution(
            [project],
            new AnalysisOptions { EnableOptimizations = false }
        );

        var found = widgets
            .Concat(boxes)
            .In(optimized)
            .Select(creation => creation.Syntax.ToString());

        Assert.Equal(
            widgets.Concat(boxes).In(exhaustive).Select(creation => creation.Syntax.ToString()),
            found
        );
        Assert.Equal(
            [
                "new Widget()",
                "new Shop.Widget()",
                "new Alias()",
                "new global::Shop.Widget()",
                "new()",
                "new Box<int>()",
            ],
            found
        );
    }

    [Fact]
    public void Creations_of_native_integer_types_include_their_keywords()
    {
        var project = fixture.Project(
            """
            class C
            {
                object[] M() => [new nint(), new System.IntPtr(), new nuint(), new System.UIntPtr()];
            }
            """
        );
        var signed = Code.ObjectCreations.Of(CodeType.Of<nint>());
        var unsigned = Code.ObjectCreations.Of(CodeType.Of<nuint>());

        var found = signed
            .Concat(unsigned)
            .In(new AnalysisSolution([project]))
            .Select(creation => creation.Syntax.ToString());

        Assert.Equal(
            ["new nint()", "new System.IntPtr()", "new nuint()", "new System.UIntPtr()"],
            found
        );
    }

    [Fact]
    public void References_to_several_members_select_each_configured_member()
    {
        var project = fixture.Project(
            """
            using System;
            class C
            {
                DateTime A => DateTime.Now;
                DateTimeOffset B => DateTimeOffset.UtcNow;
                DateTime C2 => DateTime.Today;
                string D => nameof(DateTime.Now);
            }
            """
        );
        var clock = Code.MemberReferences.To(
            CodeType.Of<DateTime>().Member(nameof(DateTime.Now)),
            CodeType.Of<DateTimeOffset>().Member(nameof(DateTimeOffset.UtcNow))
        );

        var references = clock.OutsideNameOf().In(new AnalysisSolution([project]));

        Assert.Equal(
            ["DateTime.Now", "DateTimeOffset.UtcNow"],
            references.Select(reference => reference.Syntax.ToString())
        );
    }

    private static object Describe(RuleDiagnostic diagnostic) =>
        (diagnostic.Descriptor.Id, diagnostic.Location);

    private static long? Counter(string text, string name) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
                System.Text.Json.JsonSerializer.Deserialize(
                    line["drillpress-profile ".Length..],
                    CompilationSnapshotJsonContext.Default.ProfileEvent
                )!
            )
            .Single(entry => entry.Phase == name)
            .Count;
}
