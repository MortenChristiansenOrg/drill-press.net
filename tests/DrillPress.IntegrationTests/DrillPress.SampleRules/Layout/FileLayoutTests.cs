using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.SampleRules;
using DrillPress.Testing;
using Xunit;

namespace DrillPress.IntegrationTests.SampleRules.Layout;

public sealed class FileLayoutTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public async Task A_file_with_one_type_is_renamed_after_it()
    {
        var workspace = fixture.Workspace();
        const string text = "namespace Orders;\n\npublic record Invoice(string Number);\n";
        workspace.AddProject(
            "Orders",
            [new("src/Orders/Misnamed.cs", text)],
            projectDirectory: "src/Orders"
        );

        var result = await workspace.CheckAsync(
            SampleRuleCatalog.Create(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            """
            DP1007 Name the file after its only top-level type.
            src/Orders/Misnamed.cs
              +1

            """,
            result.Output
        );
        Assert.Equal(
            new TestSource("src/Orders/Invoice.cs", text),
            result.FixedSource("src/Orders/Misnamed.cs")
        );
    }

    [Theory]
    [InlineData("public class A {} public class B {}", "")]
    [InlineData("public class Box<T> {}", "")]
    [InlineData("public class Misnamed {} ", "")]
    [InlineData("public partial class A {} public partial class A {}", "DP1007")]
    public async Task Grouped_generic_and_matching_files_keep_their_names(
        string text,
        string expected
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Orders",
            [new("src/Orders/Misnamed.cs", text)],
            projectDirectory: "src/Orders"
        );

        var result = await workspace.CheckAsync(
            SampleRuleCatalog.Create(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(expected, string.Join(",", result.Findings.Select(finding => finding.Rule)));
    }

    [Fact]
    public async Task A_file_with_top_level_statements_keeps_its_name()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Orders",
            [new("src/Orders/Misnamed.cs", "System.Console.WriteLine(); class Helper {}")],
            projectDirectory: "src/Orders",
            outputKind: Microsoft.CodeAnalysis.OutputKind.ConsoleApplication
        );

        var result = await workspace.CheckAsync(
            SampleRuleCatalog.Create(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("", result.Output);
    }

    [Fact]
    public async Task A_linked_or_explicitly_listed_file_keeps_its_name()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Orders", [new("src/Orders/Misnamed.cs", "public class Invoice {}")]);

        var result = await workspace.CheckAsync(
            SampleRuleCatalog.Create(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("", result.Output);
    }

    [Fact]
    public async Task A_test_file_moves_to_the_folder_of_its_production_file()
    {
        var workspace = fixture.Workspace();
        var orders = workspace.AddProject(
            "Orders",
            [
                new(
                    "src/Orders/Services/OrderService.cs",
                    "namespace Orders.Services;\n\npublic class OrderService {}\n"
                ),
            ],
            projectDirectory: "src/Orders"
        );
        const string test = "namespace Orders.Tests;\n\npublic class OrderServiceTests {}\n";
        workspace.AddProject(
            "Orders.Tests",
            [new("tests/Orders.Tests/OrderServiceTests.cs", test)],
            isTest: true,
            dependencies: [orders],
            projectDirectory: "tests/Orders.Tests"
        );

        var result = await workspace.CheckAsync(
            SampleRuleCatalog.Create(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            """
            DP1008 Place the test file in the folder of the production file it tests.
            tests/Orders.Tests/OrderServiceTests.cs
              +1

            """,
            result.Output
        );
        Assert.Equal(
            new TestSource("tests/Orders.Tests/Services/OrderServiceTests.cs", test),
            result.FixedSource("tests/Orders.Tests/OrderServiceTests.cs")
        );
    }

    [Fact]
    public async Task A_test_of_an_unreferenced_or_ambiguous_production_type_stays()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Orders",
            [
                new(
                    "src/Orders/Services/OrderService.cs",
                    "namespace Orders.Services; public class OrderService {}"
                ),
                new(
                    "src/Orders/Legacy/OrderService.cs",
                    "namespace Orders.Legacy; public class OrderService {}"
                ),
            ],
            projectDirectory: "src/Orders"
        );
        workspace.AddProject(
            "Billing",
            [new("src/Billing/Invoices/Invoice.cs", "public class Invoice {}")],
            projectDirectory: "src/Billing"
        );
        workspace.AddProject(
            "Orders.Tests",
            [
                new("tests/Orders.Tests/OrderServiceTests.cs", "public class OrderServiceTests {}"),
                new("tests/Orders.Tests/InvoiceTests.cs", "public class InvoiceTests {}"),
            ],
            isTest: true,
            projectDirectory: "tests/Orders.Tests"
        );

        var result = await workspace.CheckAsync(
            SampleRuleCatalog.Create(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("", result.Output);
    }
}
