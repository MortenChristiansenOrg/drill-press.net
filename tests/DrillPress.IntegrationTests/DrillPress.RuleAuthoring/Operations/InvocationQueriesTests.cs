using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class InvocationQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
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
        var calls = OperationQueries.Invocations.Calling(CodeType.Named("C").Member("Send"));

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

        var receivers = OperationQueries
            .Invocations.Calling(CodeType.Named("C").Member("Send"))
            .WhereReceiver(receiver => receiver.TypeIs(CodeType.Named("C")))
            .In(solution)
            .Select(call => $"{call.Receiver!.IsImplicit}:{call.IsConditional}")
            .ToArray();
        var variance = OperationQueries
            .Invocations.WhereReceiver(receiver =>
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
        var selected = OperationQueries.Invocations.Calling(CodeType.Named("C").Member("Send"));
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
        var selected = OperationQueries
            .Invocations.Calling(CodeType.Named("Client").Member("Send"))
            .WhereReceiver(receiver => receiver.TypeIsOrDerivesFrom(CodeType.Named("Client")));
        var extensions = OperationQueries
            .Invocations.Calling(CodeType.Named("Extensions").Member("Ping"))
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

        var actual = OperationQueries
            .Invocations.Calling(member)
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
        var selected = OperationQueries
            .Invocations.Calling(CodeType.Named("C").Member("Send"))
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
}
