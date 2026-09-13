using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Operations;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Semantics;

public sealed class CodeMemberTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Type_owned_members_select_fields_properties_and_exact_overloads()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "Api.cs",
                    """
                    using Alias = Api;
                    class Api
                    {
                        public static int Value;
                        public static int Current => 1;
                        public static void Write() { }
                        public static void Write(int value) { }
                    }
                    class Consumer
                    {
                        void Run()
                        {
                            _ = Alias.Value;
                            _ = Alias.Current;
                            Alias.Write();
                            Alias.Write(1);
                        }
                    }
                    """
                ),
            ]
        );
        var type = CodeType.Named("Api", "Library");
        var family = type.Member("Write");
        var noArguments = family.WithParameters();
        CodeType[] parameters = [CodeType.Of<int>()];
        var oneArgument = type.Member("Write", parameters);
        parameters[0] = CodeType.Of<string>();
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var references = new[]
        {
            type.Member("Value"),
            type.Member("Current"),
            family,
            noArguments,
            oneArgument,
        }
            .Select(member =>
                member
                    .References.In(solution)
                    .Select(reference => reference.Symbol!.ToDisplayString())
                    .ToArray()
            )
            .ToArray();

        Assert.Equal<string[]>(
            [
                ["Api.Value"],
                ["Api.Current"],
                ["Api.Write()", "Api.Write(int)"],
                ["Api.Write()"],
                ["Api.Write(int)"],
            ],
            references
        );
        Assert.Same(family.References, family.References);
        Assert.Equal(type, oneArgument.DeclaringType);
        Assert.Empty(type.Member("Value").WithParameters().References.In(solution));
        Assert.Empty(
            CodeType.Named("Api", "OtherAssembly").Member("Write").References.In(solution)
        );
    }

    [Fact]
    public void Fluent_members_preserve_constructed_types_and_reduced_extension_parameters()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "Calls.cs",
                    """
                    using System.Collections.Generic;
                    static class Extensions { public static void Save(this string value, int count) { } }
                    class C
                    {
                        void Run()
                        {
                            new List<string>().Add("text");
                            new List<int>().Add(1);
                            "text".Save(2);
                        }
                    }
                    """
                ),
            ]
        );
        var addText = CodeType
            .Of<List<string>>()
            .Member("Add")
            .WithParameters(CodeType.Of<string>());
        var save = CodeType
            .Named("Extensions")
            .Member("Save")
            .WithParameters(CodeType.Of<string>(), CodeType.Of<int>());

        var calls = OperationQueries.Invocations.In(
            workspace.Analyze(TestContext.Current.CancellationToken)
        );

        Assert.Equal([true, false, false], calls.Select(call => call.Calls(addText)));
        Assert.Equal([false, false, true], calls.Select(call => call.Calls(save)));
    }
}
