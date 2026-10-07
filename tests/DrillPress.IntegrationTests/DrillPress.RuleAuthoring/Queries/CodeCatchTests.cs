using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class CodeCatchTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Catch_clauses_describe_caught_types_filters_bodies_and_rethrows()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    """
                    using System;
                    using System.IO;
                    class A
                    {
                        void M()
                        {
                            try { } catch { }
                            try { } catch (Exception error) when (error.Message != "") { Log(error); throw; }
                            try { } catch (IOException) { /* ignored */ }
                            try { } catch (InvalidOperationException) { try { } catch (Exception) { throw; } }
                        }
                        void Log(Exception error) { }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var catches = Code
            .Catches.In(solution)
            .Select(clause =>
                $"{clause.Location.Line}:{clause.Location.Column}:{clause.ExceptionType?.Name}:{clause.VariableName}:{clause.CatchesAnyException}:{clause.HasFilter}:{clause.IsEmpty}:{clause.Rethrows}:{clause.Catches<IOException>()}"
            )
            .ToArray();

        Assert.Equal(
            [
                "7:17:::True:False:True:False:False",
                "8:17:Exception:error:True:True:False:True:False",
                "9:17:IOException::False:False:True:False:True",
                "10:17:InvalidOperationException::False:False:False:False:False",
                "10:61:Exception::True:False:False:True:False",
            ],
            catches
        );
    }

    [Fact]
    public async Task Empty_handlers_can_be_reported_at_their_header()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A\n{\n    void M()\n    {\n        try { } catch (System.Exception error) when (error != null) { }\n    }\n}\n"
                ),
            ]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("ERR001", "Handle or rethrow the exception.")
            .For(Code.Catches.Where(clause => clause.IsEmpty))
            .Forbid();

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new TestFinding(
                    "ERR001",
                    "A.cs",
                    5,
                    17,
                    "catch (System.Exception error) when (error != null)",
                    false
                ),
            ],
            result.Findings
        );
    }

    [Fact]
    public void Requires_an_exception_type_argument()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [new("A.cs", "class A { void M() { try { } catch { } } }")]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var clause = Assert.Single(Code.Catches.In(solution));

        Assert.False(clause.Catches(CodeType.Of<System.IO.IOException>()));
        Assert.Null(clause.Filter);
        Assert.True(clause.Body.IsEmpty);
    }
}
