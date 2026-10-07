using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class InvocationQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Multi_role_value_filters_include_either_extension_input_and_preserve_evaluation_order()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Calls",
            [
                new(
                    "Calls.cs",
                    """
                    using System.Collections.Generic;
                    class Query : List<int> {}
                    static class Ops { public static bool Compare(this IEnumerable<int> first, IEnumerable<int> second) => true; }
                    class C { void M(List<int> items, Query query) {
                        items.Compare(query); query.Compare(items); Ops.Compare(second: query, first: items);
                        query.Compare(query); items.Compare(items); unknown.Compare(query);
                    } }
                    """
                ),
            ],
            allowErrors: true
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var selected = Code.Calls.WhereAnyArgument(
            named: ["first", "second"],
            value: expression => expression.TypeIsAssignableTo(CodeType.Named("Query"))
        );

        var calls = selected
            .In(solution)
            .Select(call => call.Operation.Syntax.ToString())
            .ToArray();
        var values = selected
            .ArgumentsFor("first", "second", "first")
            .SourceValues(includeReceivers: true)
            .In(solution)
            .Select(expression => expression.Syntax.ToString())
            .ToArray();
        var roles = selected
            .In(solution)[2]
            .ArgumentsFor("first", "second")
            .Select(argument => argument.Parameter.Name)
            .ToArray();

        Assert.Equal(
            [
                "items.Compare(query)",
                "query.Compare(items)",
                "Ops.Compare(second: query, first: items)",
                "query.Compare(query)",
            ],
            calls
        );
        Assert.Equal(
            ["items", "query", "query", "items", "query", "items", "query", "query"],
            values
        );
        Assert.Equal(["second", "first"], roles);
    }

    [Fact]
    public void Multi_role_selection_distinguishes_defaults_empty_params_and_expanded_source_values()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Calls",
            [
                new(
                    "Calls.cs",
                    """
                    using System.Collections.Generic;
                    class Query : List<int> {}
                    class C {
                        static void P(params IEnumerable<int>[] source) {}
                        static void D(IEnumerable<int>? source = null) {}
                        void M(Query query, List<int> items) { P(query, items); P(); D(); D(query); }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var calls = Code
            .Calls.WhereAnyArgument(
                ["source", "missing"],
                expression => expression.TypeIsAssignableTo(CodeType.Named("Query"))
            )
            .In(solution)
            .Select(call => call.Operation.Syntax.ToString())
            .ToArray();
        var values = Code
            .Calls.ArgumentsFor("source", "missing")
            .SourceValues(includeReceivers: true)
            .In(solution)
            .Select(expression => expression.Syntax.ToString())
            .ToArray();

        Assert.Equal(["P(query, items)", "D(query)"], calls);
        Assert.Equal(["query", "items", "query"], values);
    }

    [Fact]
    public void Bound_owner_families_distinguish_inheritance_overrides_and_extension_receivers()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Calls",
            [
                new(
                    "Calls.cs",
                    """
                    class Base { public virtual void FindAsync() {} public void Load() {} }
                    class Derived : Base { public override void FindAsync() {} }
                    class Other { public void FindAsync() {} }
                    static class Extensions { public static void FindAsync(this Base source) {} }
                    class C {
                        void M(Base b, Derived d, Other other) {
                            b.FindAsync(); d.FindAsync(); d.Load(); other.FindAsync();
                            Extensions.FindAsync(d);
                        }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var owner = CodeType.Named("Base", "Calls");

        var exact = Code
            .Calls.ToMethodsDeclaredOn(owner)
            .In(solution)
            .Select(call => call.Operation.Syntax.ToString())
            .ToArray();
        var hierarchy = Code
            .Calls.ToMethodsDeclaredOnOrDerivedFrom(owner)
            .ToMethodsMatching("*Async")
            .In(solution)
            .Select(call => call.Operation.Syntax.ToString())
            .ToArray();
        var extensions = Code
            .Calls.ToMethodsDeclaredOn(CodeType.Named("Extensions", "Calls"))
            .In(solution);

        Assert.Equal(["b.FindAsync()", "d.Load()"], exact);
        Assert.Equal(["b.FindAsync()", "d.FindAsync()"], hierarchy);
        Assert.True(Assert.Single(extensions).IsDeclaredOn(CodeType.Named("Extensions", "Calls")));
        Assert.False(Assert.Single(extensions).IsDeclaredOnOrDerivedFrom(owner));
    }

    [Fact]
    public void Bound_owner_filters_preserve_metadata_generic_identity_and_both_extension_spellings()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Calls",
            [
                new(
                    "Calls.cs",
                    """
                    using System.Linq;
                    class C {
                        void M(System.Collections.Generic.List<string> strings, System.Collections.Generic.List<int> numbers) {
                            strings.Clear(); numbers.Clear(); strings.Count(); Enumerable.Count(strings); unknown.Count();
                        }
                    }
                    """
                ),
            ],
            allowErrors: true
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var lists = Code
            .Calls.ToMethodsDeclaredOn(CodeType.Of<List<string>>())
            .In(solution)
            .Select(call => call.Operation.Syntax.ToString())
            .ToArray();
        var counts = Code
            .Calls.ToMethodsDeclaredOn(CodeType.Framework("System.Linq.Enumerable"))
            .ToMethodsNamed("Count")
            .In(solution)
            .Select(call => call.Operation.Syntax.ToString())
            .ToArray();

        Assert.Equal(["strings.Clear()"], lists);
        Assert.Equal(["strings.Count()", "Enumerable.Count(strings)"], counts);
    }

    [Fact]
    public void Expanded_params_collections_expose_each_input_and_keep_empty_groups()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Calls",
            [
                new(
                    "Calls.cs",
                    """
                    class C {
                        void Send(params System.ReadOnlySpan<int> values) {}
                        void M() { Send(1, 2); Send(); }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var calls = Code.Calls.To(CodeType.Named("C").Member("Send"));

        var values = calls
            .ArgumentsFor("values")
            .SourceValues()
            .In(solution)
            .Select(value => value.Syntax.ToString())
            .ToArray();
        var counts = calls
            .In(solution)
            .Select(call => call.Parameter("values")!.Values.Count)
            .ToArray();

        Assert.Equal(["1", "2"], values);
        Assert.Equal([2, 0], counts);
    }

    [Fact]
    public void Implicit_this_conditional_access_and_generic_variance_use_actual_receiver_evidence()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Calls",
            [
                new(
                    "Calls.cs",
                    """
                    class C {
                        void Send() {}
                        void M(C? other, System.Collections.Generic.IEnumerable<string> values) {
                            Send(); other?.Send(); values.GetEnumerator();
                        }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var receivers = Code
            .Calls.To(CodeType.Named("C").Member("Send"))
            .WhereReceiver(receiver => receiver.TypeIs(CodeType.Named("C")))
            .In(solution)
            .Select(call => $"{call.Receiver!.IsImplicit}:{call.IsConditional}")
            .ToArray();
        var variance = Code
            .Calls.WhereReceiver(receiver =>
                receiver.TypeIsAssignableTo(CodeType.Of<IEnumerable<object>>())
            )
            .In(solution)
            .Select(call => call.Operation.Syntax.ToString())
            .ToArray();

        Assert.Equal(["True:False", "False:True"], receivers);
        Assert.Equal(["values.GetEnumerator()"], variance);
    }

    [Fact]
    public void Named_defaults_and_expanded_arguments_keep_parameter_and_source_order_separate()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Calls",
            [
                new(
                    "Calls.cs",
                    """
                    class C {
                        void Send(string url, int mode = 2, params int[] values) { }
                        void M() { Send(mode: 2, url: "a"); Send("b", 2, 3, 4); Send("c"); }
                    }
                    """
                ),
            ]
        );
        var selected = Code.Calls.To(CodeType.Named("C").Member("Send"));
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var arguments = selected
            .In(solution)
            .SelectMany(call =>
                call.Arguments.Select(argument =>
                    $"{argument.Parameter.Name}:{argument.SourceIndex}:{argument.Kind}:{argument.Value?.Syntax}:{argument.Constant}"
                )
            )
            .ToArray();

        Assert.Equal(
            [
                "mode:0:Explicit:2:2",
                "url:1:Explicit:\"a\":a",
                "url:0:Explicit:\"b\":b",
                "mode:1:Explicit:2:2",
                "values:2:ParamArray:3:3",
                "values:3:ParamArray:4:4",
                "url:0:Explicit:\"c\":c",
                "mode::DefaultValue::2",
            ],
            arguments
        );
    }

    [Fact]
    public void Receiver_inheritance_keeps_the_selected_member_identity()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Calls",
            [
                new(
                    "Calls.cs",
                    """
                    class Client { public void Send(string url) {} }
                    class Derived : Client {}
                    class Other { public void Send(string url) {} }
                    static class Extensions { public static void Ping(this Client client, string url) {} }
                    class C { void M(Derived d, Other other) { d.Send("a"); other.Send("b"); d.Ping("c"); Extensions.Ping(d, "e"); } }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var selected = Code
            .Calls.To(CodeType.Named("Client").Member("Send"))
            .WhereReceiver(receiver => receiver.TypeIsOrDerivesFrom(CodeType.Named("Client")));
        var extensions = Code
            .Calls.To(CodeType.Named("Extensions").Member("Ping"))
            .WhereReceiver(receiver => receiver.TypeIsOrDerivesFrom(CodeType.Named("Client")));

        var ordinary = selected
            .In(solution)
            .Select(call => call.Operation.Syntax.ToString())
            .ToArray();
        var receiverValues = extensions
            .In(solution)
            .Select(call => call.Receiver!.Syntax.ToString())
            .ToArray();

        Assert.Equal(["d.Send(\"a\")"], ordinary);
        Assert.Equal(["d", "d"], receiverValues);
    }

    [Fact]
    public void Signature_refinements_preserve_actual_generic_arguments_and_reject_error_calls()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Calls",
            [
                new(
                    "Calls.cs",
                    """
                    class C {
                        static T Echo<T>(T value) => value;
                        void M() { Echo("a"); Echo(1); Echo(unknown); }
                    }
                    """
                ),
            ],
            allowErrors: true
        );
        var member = CodeType
            .Named("C", "Calls")
            .Member("Echo")
            .WithSignature(
                new(
                    genericArity: 1,
                    isStatic: true,
                    returnType: CodeType.Of<string>(),
                    typeArguments: [CodeType.Of<string>()]
                )
            );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var actual = Code
            .Calls.To(member)
            .In(solution)
            .Select(call => call.Operation.Syntax.ToString())
            .ToArray();

        Assert.Equal(["Echo(\"a\")"], actual);
    }

    [Fact]
    public void Typed_constants_do_not_equate_unrelated_enums_and_implicit_conversions_stay_visible()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Calls",
            [
                new(
                    "Calls.cs",
                    """
                    enum First { Value = 1 } enum Second { Value = 1 }
                    class C { void Send(object value) {} void M() { Send(First.Value); Send(Second.Value); } }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var selected = Code
            .Calls.To(CodeType.Named("C").Member("Send"))
            .WhereArgument(
                "value",
                argument => argument.Value?.IsConstant(CodeType.Named("First"), 1) == true
            );

        var values = selected
            .ArgumentsFor("value")
            .In(solution)
            .Select(argument =>
                $"{argument.Value!.TypeInfo.Type}:{argument.Value.TypeInfo.ConvertedType}:{argument.Value.Conversion.IsBoxing}"
            )
            .ToArray();

        Assert.Equal(["First:object:True"], values);
    }

    [Fact]
    public async Task Omitted_default_arguments_report_at_their_call()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Calls",
            [
                new(
                    "Calls.cs",
                    "class C\n{\n    void Send(int timeout = -1) { }\n\n    void M()\n    {\n        Send();\n        Send(timeout: -1);\n        Send(5);\n    }\n}\n"
                ),
            ]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("TIMEOUT", "Pass a bounded timeout.")
            .For(Code.Calls.ToMethodsNamed("Send").ArgumentsFor("timeout"))
            .Require(argument => !argument.Is(-1));

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            TIMEOUT Pass a bounded timeout.
            Calls.cs
              7:9
              8:23

            """.ReplaceLineEndings("\n"),
            result.Output
        );
    }
}
