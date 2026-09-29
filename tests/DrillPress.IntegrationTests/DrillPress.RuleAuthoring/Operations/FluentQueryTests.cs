using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class FluentQueryTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Optional_defaults_and_missing_parameters_remain_distinct()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Calls",
            [
                new(
                    "Calls.cs",
                    """
                    using System;
                    class C {
                        static void Send(string value) {}
                        static void Send(int value, StringComparison comparison = StringComparison.OrdinalIgnoreCase) {}
                        void M() { Send("x"); Send(1); Send(2, StringComparison.Ordinal); Send(3, StringComparison.OrdinalIgnoreCase); }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var calls = OperationQueries.Invocations.Calling(CodeType.Named("C").Member("Send"));

        var union = calls
            .ArgumentsFor("comparison")
            .Union(
                OperationQueries
                    .Invocations.Calling(CodeType.Named("C").Member("Send"))
                    .ArgumentsFor("comparison")
            )
            .In(solution)
            .Select(argument => argument.IsExplicit)
            .ToArray();
        var effective = calls
            .WhereArgumentOrMissing("comparison", argument => argument.Is(StringComparison.Ordinal))
            .In(solution)
            .Select(call => call.Operation.Syntax.ToString())
            .ToArray();
        var omitted = calls
            .WhereArgumentOrMissing(
                "comparison",
                argument => argument.IsOmittedOr(StringComparison.Ordinal)
            )
            .In(solution)
            .Select(call => call.Operation.Syntax.ToString())
            .ToArray();

        Assert.Equal([false, true, true], union);
        Assert.Equal(["Send(\"x\")", "Send(2, StringComparison.Ordinal)"], effective);
        Assert.Equal(["Send(\"x\")", "Send(1)", "Send(2, StringComparison.Ordinal)"], omitted);
    }

    [Fact]
    public void Receiver_names_and_parameter_types_include_both_extension_spellings()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Calls",
            [
                new(
                    "Calls.cs",
                    """
                    using System;
                    class Client { public void Send(string url) {} public void Send(int url) {} }
                    class Derived : Client {}
                    static class Extensions { public static void Get(this Client client, Uri requestUri) {} }
                    class C { void M(Derived client) {
                        client.Send("/a"); client?.Send("/b"); client!.Send(1);
                        client.Get(new Uri("/c", UriKind.Relative));
                        Extensions.Get(client, new Uri("/d", UriKind.Relative));
                        Action later = () => client.Send("/e");
                    } }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var values = OperationQueries
            .Invocations.OnReceiverOfType(CodeType.Named("Client"))
            .ToMethodsNamed("Send", "Get")
            .ArgumentsOfTypes([CodeType.Of<string>(), CodeType.Of<Uri>()], "url", "requestUri")
            .SourceValues()
            .In(solution)
            .Select(value => value.Syntax.ToString())
            .ToArray();

        Assert.Equal(
            [
                "\"/a\"",
                "\"/b\"",
                "new Uri(\"/c\", UriKind.Relative)",
                "new Uri(\"/d\", UriKind.Relative)",
                "\"/e\"",
            ],
            values
        );
    }

    [Fact]
    public void Scope_and_base_filters_preserve_segment_and_strict_ancestry_boundaries()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Api",
            [
                new(
                    "Endpoints/RootTests.cs",
                    "namespace Contoso.Api; class Base<T> { public void Handle() {} }"
                ),
                new(
                    "Feature/Endpoints/ChildTests.cs",
                    "namespace Contoso.Api.Child; class C : Base<int>, System.IDisposable { public new void Handle() {} public void Dispose() {} }"
                ),
                new(
                    "EndpointsExtra/OtherTests.cs",
                    "namespace Contoso.ApiTools; class D : Contoso.Api.Base<int> { public new void Handle() {} }"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var type = CodeType.Named("Contoso.Api.Base<>");

        var names = Code
            .Methods.InProject("Api")
            .InFolder("Endpoints")
            .InFilesNamed("*Tests.cs")
            .InNamespace("Contoso.Api.**")
            .Named("Handle")
            .DeclaredInTypesDerivedFrom(type)
            .In(solution)
            .Select(method => method.ContainingType!.Name)
            .ToArray();
        var owners = Code
            .Types.InNamespace("Contoso.Api.**")
            .In(solution)
            .Select(owner => owner.Name)
            .ToArray();
        var implementations = Code
            .Types.DerivedFrom(type)
            .ImplementingInterface(CodeType.Of<IDisposable>())
            .In(solution)
            .Select(owner => owner.Name)
            .ToArray();

        Assert.Equal(["C"], names);
        Assert.Equal(["Base", "C"], owners);
        Assert.Equal(["C"], implementations);
    }

    [Fact]
    public void Union_deduplicates_occurrences_and_group_membership_keeps_ambiguity()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Calls",
            [
                new(
                    "Calls.cs",
                    "class C { string A = \"same\"; string B = \"same\"; string Cc = \"alone\"; }"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var expressions = Sources
            .Nodes<LiteralExpressionSyntax>()
            .Select(node => new CodeExpression(node.Source, node.Syntax));
        var groups = ExpressionGroups.Constants(expressions);
        var duplicateGroups = groups.Concat(ExpressionGroups.Constants(expressions));

        var counts = new[]
        {
            expressions.Union(expressions).In(solution).Count,
            expressions.Concat(expressions).In(solution).Count,
        };
        var membership = expressions
            .WithGroupsFrom(duplicateGroups)
            .In(solution)
            .Select(value =>
                $"{value.Expression.TextValue}:{value.Groups.Count}:{value.UniqueGroup is null}"
            )
            .ToArray();
        var unique = expressions
            .WithGroupsFrom(groups)
            .In(solution)
            .Select(value => value.UniqueGroup?.Occurrences.Count ?? 0)
            .ToArray();

        Assert.Equal([3, 6], counts);
        Assert.Equal(["same:2:True", "same:2:True", "alone:0:True"], membership);
        Assert.Equal([2, 2, 0], unique);
    }

    [Fact]
    public void Typed_attribute_values_distinguish_missing_false_and_enum_identity()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Calls",
            [
                new(
                    "Calls.cs",
                    """
                    using System;
                    class MarkAttribute(string text, StringComparison comparison) : Attribute {
                        public bool Flag { get; set; } = true;
                        public int[] Values { get; set; } = [];
                    }
                    [Mark("hello", StringComparison.Ordinal, Flag = false, Values = new[] { 1 })] class C {}
                    [Mark("hello", StringComparison.Ordinal)] class D {}
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var marker = CodeType.Named("MarkAttribute");

        var names = Code
            .Types.Where(type =>
                type.HasAttribute(
                    marker,
                    attribute =>
                        attribute.ConstructorValue<string>("text").Value == "hello"
                        && attribute.ConstructorValue<StringComparison>("comparison").Value
                            == StringComparison.Ordinal
                        && !attribute.ConstructorValue<int>("comparison").HasValue
                        && !attribute.NamedValue<int[]>("Values").HasValue
                        && !attribute.FlagOrDefault("Flag", true)
                )
            )
            .In(solution)
            .Select(type => type.Name)
            .ToArray();

        Assert.Equal(["C"], names);
    }

    [Fact]
    public void Overload_families_and_typed_defaults_do_not_match_object_equals()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Calls",
            [
                new(
                    "Calls.cs",
                    """
                    using System;
                    class C { void M(string a, string b) {
                        string.Equals(a, b); string.Equals(a, b, StringComparison.Ordinal);
                        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
                        a.Equals(b); a.Equals(b, StringComparison.Ordinal); a.Equals((object)b);
                    } }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var equals = CodeType.Of<string>().Member("Equals");
        var family = equals
            .WithParameters(CodeType.Of<string>(), CodeType.Of<string>())
            .OptionallyFollowedBy<StringComparison>()
            .Union(
                equals
                    .WithParameters(CodeType.Of<string>())
                    .OptionallyFollowedBy<StringComparison>()
            );

        var matches = OperationQueries
            .Invocations.Calling(family)
            .WhereArgumentOrMissing(
                "comparisonType",
                argument => argument.Is(StringComparison.Ordinal)
            )
            .In(solution)
            .Select(call => call.Operation.Syntax.ToString())
            .ToArray();

        Assert.Equal(
            [
                "string.Equals(a, b)",
                "string.Equals(a, b, StringComparison.Ordinal)",
                "a.Equals(b)",
                "a.Equals(b, StringComparison.Ordinal)",
            ],
            matches
        );
    }

    [Fact]
    public void Body_call_predicates_observe_the_nested_function_boundary()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Calls",
            [
                new(
                    "Calls.cs",
                    """
                    abstract class C {
                        static void Set(string key) {}
                        public abstract void Missing();
                        void M() { void Local() { Set("Name"); } }
                        void N() { Set(nameof(Name)); }
                        string Name = "";
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var target = CodeType.Named("C").Member("Set");

        var ordinary = Code
            .Methods.Body()
            .In(solution)
            .Select(body => body.Calls(target, withArgument: "key", equalTo: "Name"))
            .ToArray();
        var included = Code
            .Methods.Named("M")
            .Body(NestedFunctions.Include)
            .In(solution)
            .Select(body => body.Calls(target, withArgument: "key", equalTo: "Name"))
            .ToArray();

        Assert.Equal([false, false, true], ordinary);
        Assert.Equal([true], included);
    }

    [Fact]
    public void Member_argument_selection_accepts_parentheses_and_rejects_computed_ancestors()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Calls",
            [
                new(
                    "Calls.cs",
                    """
                    using System;
                    using System.Linq;
                    class C { void M(string[] values) {
                        values.Distinct((StringComparer.Ordinal));
                        Enumerable.Distinct(values, comparer: StringComparer.Ordinal);
                        values.Distinct(true ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
                    } }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var matches = CodeType
            .Of<StringComparer>()
            .Member("Ordinal")
            .References.PassedAs("comparer")
            .To(CodeType.Named("System.Linq.Enumerable").Member("Distinct"))
            .In(solution)
            .Select(argument => argument.Invocation.Operation.Syntax.ToString())
            .ToArray();

        Assert.Equal(
            [
                "values.Distinct((StringComparer.Ordinal))",
                "Enumerable.Distinct(values, comparer: StringComparer.Ordinal)",
            ],
            matches
        );
    }
}
