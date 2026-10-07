using DrillPress.Testing;
using Xunit;

namespace MyRules.Tests;

public sealed class ExampleRulesTests
{
    [Fact]
    public async Task Console_output_is_reported_outside_test_projects_only()
    {
        var source = "class Worker\n{\n    void Run() => System.Console.WriteLine(\"start\");\n}\n";

        var production = await CheckAsync(source);
        var tests = await CheckAsync(source, isTest: true);

        Assert.Equal(
            """
            TEAM001 Use the application logger instead of Console.WriteLine.
            App.cs
              3:19

            """.ReplaceLineEndings("\n"),
            production.Output
        );
        Assert.Equal("", tests.Output);
    }

    [Fact]
    public async Task Async_methods_require_the_suffix()
    {
        var source = """
            using System.Threading.Tasks;

            class Jobs
            {
                async Task Load() => await Task.Delay(1).ConfigureAwait(false);

                async Task SaveAsync() => await Task.Delay(1).ConfigureAwait(true);
            }

            """;

        var result = await CheckAsync(source);

        Assert.Equal(
            """
            TEAM002 Pass ConfigureAwait(false) in application code.
            App.cs
              7:66
            TEAM003 Give asynchronous methods an Async suffix.
            App.cs
              5:16

            """.ReplaceLineEndings("\n"),
            result.Output
        );
    }

    [Fact]
    public async Task String_empty_becomes_a_literal_outside_nameof()
    {
        var source = """
            class Names
            {
                string Value => string.Empty;

                string Name => nameof(string.Empty);
            }

            """;

        var result = await CheckAsync(source);

        Assert.Equal(
            """
            TEAM005 Use "" instead of string.Empty.
            App.cs
              +3:21

            """.ReplaceLineEndings("\n"),
            result.Output
        );
        Assert.Equal(source.Replace("=> string.Empty", "=> \"\""), result.FixedText("App.cs"));
    }

    [Fact]
    public async Task Style_fixes_add_braces_and_remove_the_default_modifier()
    {
        var source = """
            internal sealed class Guard
            {
                int Check(int value)
                {
                    if (value < 0)
                        return 0;
                    return value;
                }
            }

            """;

        var result = await CheckAsync(source);

        Assert.Equal(
            """
            TEAM006 Add braces to if and else branches.
            App.cs
              +6:13
            TEAM007 Omit the default internal modifier on top-level types.
            App.cs
              +1

            """.ReplaceLineEndings("\n"),
            result.Output
        );
        Assert.Equal(
            """
            sealed class Guard
            {
                int Check(int value)
                {
                    if (value < 0)
                    {
                        return 0;
                    }
                    return value;
                }
            }

            """,
            result.FixedText("App.cs")
        );
    }

    [Fact]
    public async Task Public_types_need_documentation_and_handlers_cannot_be_empty()
    {
        var source = """
            /// <summary>Documented.</summary>
            public class Api
            {
                void Run()
                {
                    try { }
                    catch (System.Exception) { }
                }
            }

            public class Undocumented { }

            """;

        var result = await CheckAsync(source);

        Assert.Equal(
            """
            TEAM004 Document public types.
            App.cs
              11:14
            TEAM008 Handle, log, or rethrow caught exceptions.
            App.cs
              7:9

            """.ReplaceLineEndings("\n"),
            result.Output
        );
    }

    private static async Task<RuleTestResult> CheckAsync(string source, bool isTest = false)
    {
        var workspace = new RuleTestWorkspace();
        workspace.AddProject("App", [new TestSource("App.cs", source)], isTest: isTest);
        return await workspace.CheckAsync(
            ExampleRules.Create(),
            TestContext.Current.CancellationToken
        );
    }
}
