using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Testing;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class FileMoveTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    private const string Service = "namespace Orders;\n\npublic class OrderService {}\n";

    [Fact]
    public async Task A_file_is_renamed_after_its_type_with_unchanged_text()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Orders",
            [new("src/Orders/Service.cs", Service)],
            projectDirectory: "src/Orders"
        );

        var result = await workspace.CheckAsync(
            RenameRules(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            """
            NAME Name the file after its type.
            src/Orders/Service.cs
              +1

            """,
            result.Output
        );
        Assert.Equal("src/Orders/OrderService.cs", result.FixedPath("src/Orders/Service.cs"));
        Assert.Equal(Service, result.FixedText("src/Orders/OrderService.cs"));
        Assert.Equal(
            new TestSource("src/Orders/OrderService.cs", Service),
            result.FixedSource("src/Orders/Service.cs")
        );
    }

    [Fact]
    public async Task A_renamed_file_rechecks_clean()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Orders",
            [new("src/Orders/Service.cs", Service)],
            projectDirectory: "src/Orders"
        );
        var result = await workspace.CheckAsync(
            RenameRules(),
            TestContext.Current.CancellationToken
        );
        var fixedWorkspace = fixture.Workspace();
        fixedWorkspace.AddProject(
            "Orders",
            [result.FixedSource("src/Orders/Service.cs")],
            projectDirectory: "src/Orders"
        );

        var recheck = await fixedWorkspace.CheckAsync(
            RenameRules(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("", recheck.Output);
    }

    [Fact]
    public async Task A_test_file_moves_to_the_folder_mirroring_its_production_file()
    {
        var workspace = fixture.Workspace();
        const string test =
            "namespace Orders.Tests.Services;\n\npublic class OrderServiceTests {}\n";
        workspace.AddProject(
            "Orders.Tests",
            [new("tests/Orders.Tests/OrderServiceTests.cs", test)],
            projectDirectory: "tests/Orders.Tests"
        );
        var rules = new RuleCatalog();
        rules
            .Rule("MIRROR", "Mirror the production folder.")
            .For(Code.Files.Where(file => file.RelativePath == "OrderServiceTests.cs"))
            .Forbid(fix: file => Fix.For(file).MoveTo("Services/OrderServiceTests.cs").Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            "tests/Orders.Tests/Services/OrderServiceTests.cs",
            result.FixedPath("tests/Orders.Tests/OrderServiceTests.cs")
        );
        Assert.Equal(test, result.FixedText("tests/Orders.Tests/Services/OrderServiceTests.cs"));
    }

    [Fact]
    public async Task Every_framework_compiling_the_file_validates_the_move()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Orders",
            [new("src/Orders/Service.cs", Service)],
            projectDirectory: "src/Orders"
        );
        workspace.AddProject(
            "Orders",
            [new("src/Orders/Service.cs", Service)],
            framework: "net9.0",
            projectDirectory: "src/Orders"
        );

        var result = await workspace.CheckAsync(
            RenameRules(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("src/Orders/OrderService.cs", result.FixedPath("src/Orders/Service.cs"));
    }

    [Theory]
    [InlineData(
        null,
        "Service.cs",
        "the project does not select its sources only by the SDK's default compile glob"
    )]
    [InlineData(
        "src/Orders",
        "obj/Service.cs",
        "Orders (net10.0) would not compile Service.cs at its new path"
    )]
    [InlineData(
        "src/Orders",
        "bin/Service.cs",
        "Orders (net10.0) would not compile Service.cs at its new path"
    )]
    [InlineData(
        "src/Orders",
        ".hidden/Service.cs",
        "Orders (net10.0) would not compile Service.cs at its new path"
    )]
    [InlineData("src/Orders", "Service.g.cs", "the new file name marks generated code")]
    [InlineData("src/Orders", "Service.txt", "the new file name does not end in .cs")]
    [InlineData("src/Orders", "Other.cs", "a loaded file already has that path")]
    [InlineData("src/Orders", "OTHER.cs", "a loaded file already has that path")]
    [InlineData(
        "src/Orders",
        "Nested/Service.cs",
        "Nested (net10.0) would also compile the file at its new path"
    )]
    public async Task Moves_that_change_membership_or_collide_are_withheld(
        string? directory,
        string destination,
        string reason
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Orders",
            [
                new("src/Orders/Service.cs", Service),
                new("src/Orders/Other.cs", "namespace Orders;\n\npublic class Other {}\n"),
            ],
            projectDirectory: directory
        );
        workspace.AddProject(
            "Nested",
            [new("src/Orders/Nested/Nested.cs", "class Nested {}")],
            projectDirectory: "src/Orders/Nested"
        );

        var result = await workspace.CheckAsync(
            MoveRules("src/Orders/Service.cs", destination),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            $"""
            MOVE Move the file.
            src/Orders/Service.cs
              1
                fix withheld: {reason}

            """,
            result.ExplainedOutput
        );
        Assert.Equal("src/Orders/Service.cs", result.FixedPath("src/Orders/Service.cs"));
    }

    [Fact]
    public async Task A_linked_file_is_not_moved()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Orders",
            [new("src/Orders/Service.cs", Service)],
            projectDirectory: "src/Orders"
        );
        workspace.AddProject(
            "Billing",
            [new("src/Orders/Service.cs", Service)],
            projectDirectory: "src/Billing"
        );

        var result = await workspace.CheckAsync(
            MoveRules("src/Orders/Service.cs", "OrderService.cs", project: "Orders"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            """
            MOVE Move the file.
            src/Orders/Service.cs
              1
                fix withheld: Billing (net10.0) compiles the file through a linked or explicit Compile item

            """,
            result.ExplainedOutput
        );
    }

    [Fact]
    public async Task A_caller_file_path_withholds_the_proposed_move()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Orders",
            [new("src/Orders/Service.cs", CallerFilePathSource)],
            projectDirectory: "src/Orders"
        );

        var result = await workspace.CheckAsync(
            MoveRules("src/Orders/Service.cs", "OrderService.cs"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            """
            MOVE Move the file.
            src/Orders/Service.cs
              1
                fix withheld: the move changes the caller file path passed to Orders.Log.Write(string); use SafeWhen

            """,
            result.ExplainedOutput
        );
    }

    [Fact]
    public async Task A_proof_accepts_the_caller_file_paths_it_changes()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Orders",
            [new("src/Orders/Service.cs", CallerFilePathSource)],
            projectDirectory: "src/Orders"
        );
        var seen = new List<string>();
        var rules = new RuleCatalog();
        rules
            .Rule("MOVE", "Move the file.")
            .For(Code.Files)
            .Forbid(fix: file =>
                Fix.For(file)
                    .RenameTo("OrderService.cs")
                    .SafeWhen(change =>
                    {
                        seen.Add(
                            $"{change.From} -> {change.To}: {string.Join(", ", change.CallerFilePathMethods.Select(method => method.Name))}"
                        );
                        return change.CallerFilePathMethods.All(method =>
                            method.ContainingType.Name == "Log"
                        );
                    })
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal("src/Orders/OrderService.cs", result.FixedPath("src/Orders/Service.cs"));
        Assert.Equal(
            ["src/Orders/Service.cs -> src/Orders/OrderService.cs: Write"],
            seen.Distinct()
        );
    }

    [Fact]
    public async Task Another_rule_edits_the_moved_file_in_the_same_run()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Orders",
            [new("src/Orders/Service.cs", Service)],
            projectDirectory: "src/Orders"
        );
        var rules = RenameRules();
        rules
            .Rule("SEALED", "Seal the class.")
            .For(Code.TypeDeclarations)
            .Forbid(fix: type => Fix.For(type).AddModifier(Modifier.Sealed).Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            new TestSource(
                "src/Orders/OrderService.cs",
                "namespace Orders;\n\npublic sealed class OrderService {}\n"
            ),
            result.FixedSource("src/Orders/Service.cs")
        );
    }

    [Fact]
    public async Task Two_files_moved_to_one_path_are_both_withheld()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Orders",
            [new("src/Orders/A.cs", "class A {}"), new("src/Orders/B.cs", "class B {}")],
            projectDirectory: "src/Orders"
        );
        var rules = new RuleCatalog();
        rules
            .Rule("MOVE", "Move the file.")
            .For(Code.Files)
            .Forbid(fix: file => Fix.For(file).RenameTo("Same.cs").Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            MOVE Move the file.
            src/Orders/A.cs
              1
                fix withheld: its file move conflicts with another fix's move of the same file or to the same path
            src/Orders/B.cs
              1
                fix withheld: its file move conflicts with another fix's move of the same file or to the same path

            """,
            result.ExplainedOutput
        );
    }

    [Fact]
    public void Paths_must_stay_below_the_project_directory()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Orders",
            [new("src/Orders/Service.cs", Service)],
            projectDirectory: "src/Orders"
        );
        var file = Code.Files.In(workspace.Analyze(TestContext.Current.CancellationToken)).Single();

        var exception = Assert.Throws<ArgumentException>(() => Fix.For(file).MoveTo("../Other.cs"));

        Assert.Equal(
            "Use a relative path with forward slashes, without '.' or '..' segments. (Parameter 'relativePath')",
            exception.Message
        );
    }

    private const string CallerFilePathSource = """
        namespace Orders;

        public static class Log
        {
            public static void Write([System.Runtime.CompilerServices.CallerFilePath] string path = "") {}
        }

        public class OrderService
        {
            public void Save() => Log.Write();
        }

        """;

    private static RuleCatalog RenameRules()
    {
        var rules = new RuleCatalog();
        rules
            .Rule("NAME", "Name the file after its type.")
            .For(
                Code.Files.Where(file =>
                    file.Nodes<ClassDeclarationSyntax>().FirstOrDefault() is { } type
                    && file.Name != type.Syntax.Identifier.Text + ".cs"
                )
            )
            .Forbid(fix: file =>
                Fix.For(file)
                    .RenameTo(
                        file.Nodes<ClassDeclarationSyntax>().First().Syntax.Identifier.Text + ".cs"
                    )
                    .Propose()
            );
        return rules;
    }

    private static RuleCatalog MoveRules(string path, string destination, string? project = null)
    {
        var rules = new RuleCatalog();
        rules
            .Rule("MOVE", "Move the file.")
            .For(
                Code.Files.Where(file =>
                    file.Path == path && (project is null || file.Source.Project.Name == project)
                )
            )
            .Forbid(fix: file => Fix.For(file).MoveTo(destination).Propose());
        return rules;
    }
}
