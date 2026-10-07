using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Semantics;

public sealed class CodeTypeReferenceTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using Clock = System.DateTime;
        namespace Shop.Domain
        {
            [Obsolete("old")]
            class Order : IComparable<Order>
            {
                List<Infrastructure.Store> stores = new();
                string name = string.Empty;
                public int CompareTo(Order? other)
                {
                    var created = Clock.Now;
                    var copy = new Infrastructure.Store();
                    object boxed = (Infrastructure.Store)copy;
                    return nameof(Infrastructure.Store).Length + typeof(System.DateTime).Name.Length;
                }
            }
        }
        namespace Shop.Infrastructure
        {
            class Store { }
        }
        """;

    [Fact]
    public void Type_references_include_qualifiers_attributes_aliases_and_generic_arguments_but_not_var()
    {
        var solution = Analyze();

        var references = Code
            .TypeReferences.InNamespace("Shop.Domain")
            .In(solution)
            .Select(reference => $"{reference.Syntax}->{reference.Type.Name}")
            .ToArray();

        Assert.Equal(
            [
                "Obsolete->ObsoleteAttribute",
                "IComparable<Order>->IComparable",
                "Order->Order",
                "List<Infrastructure.Store>->List",
                "Infrastructure.Store->Store",
                "string->String",
                "string->String",
                "int->Int32",
                "Order->Order",
                "Clock->DateTime",
                "Infrastructure.Store->Store",
                "object->Object",
                "Infrastructure.Store->Store",
                "Infrastructure.Store->Store",
                "System.DateTime->DateTime",
            ],
            references
        );
    }

    [Fact]
    public void A_type_descriptor_selects_its_references_in_any_spelling()
    {
        var solution = Analyze();

        var store = CodeType.Named("Shop.Infrastructure.Store").References.In(solution);
        var outsideNameOf = CodeType
            .Named("Shop.Infrastructure.Store")
            .References.OutsideNameOf()
            .In(solution);
        var dates = CodeType.Of<DateTime>().References.In(solution);
        var obsolete = CodeType.Of<ObsoleteAttribute>().References.In(solution);
        var lists = CodeType.Named("System.Collections.Generic.List<>").References.In(solution);

        Assert.Equal(4, store.Count);
        Assert.Equal(3, outsideNameOf.Count);
        Assert.Equal(
            ["System.DateTime", "Clock", "System.DateTime"],
            dates.Select(reference => reference.Syntax.ToString())
        );
        Assert.Equal(["Obsolete"], obsolete.Select(reference => reference.Syntax.ToString()));
        Assert.Equal("List<Infrastructure.Store>", Assert.Single(lists).Syntax.ToString());
        Assert.False(lists[0].RefersTo<List<int>>());
    }

    [Fact]
    public async Task Architecture_rules_can_forbid_dependencies_between_namespaces()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Shop", [new("Order.cs", Source)]);
        var rules = new RuleSet();
        rules
            .Rule("ARCH001", "Keep the domain independent of infrastructure.")
            .For(
                Code.TypeReferences.InNamespace("Shop.Domain")
                    .Where(reference => reference.Type.IsInNamespace("Shop.Infrastructure.**"))
                    .OutsideNameOf()
            )
            .Forbid();

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            ARCH001 Keep the domain independent of infrastructure.
            Order.cs
              9:14
              14:28
              15:29

            """.Replace("\r\n", "\n"),
            result.Output
        );
    }

    private AnalysisSolution Analyze()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Shop", [new("Order.cs", Source)]);
        return workspace.Analyze(TestContext.Current.CancellationToken);
    }
}
