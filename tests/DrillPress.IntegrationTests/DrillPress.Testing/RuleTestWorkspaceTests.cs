using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.Testing;

public sealed class RuleTestWorkspaceTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Project_options_match_compilation_snapshot_and_source_directives()
    {
        var workspace = fixture.Workspace();
        var library = workspace.AddProject(
            "Annotated",
            [
                new(
                    "Library.cs",
                    "public static class Library { public static string Value => \"x\"; }"
                ),
            ]
        );
        var legacy = workspace.AddProject(
            "Legacy",
            [
                new(
                    "Legacy.cs",
                    """
                    class Legacy
                    {
                        string Read() => Library.Value;
                    #nullable enable
                        string Enabled() => Library.Value;
                    }
                    """
                ),
            ],
            dependencies: [library],
            nullable: Microsoft.CodeAnalysis.NullableContextOptions.Disable,
            languageVersion: Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp12
        );
        var source = legacy.Sources.Single();

        var contexts = new[]
        {
            source.Model.GetNullableContext(
                source.Document.Text.IndexOf("Read()", StringComparison.Ordinal)
            ),
            source.Model.GetNullableContext(
                source.Document.Text.IndexOf("Enabled()", StringComparison.Ordinal)
            ),
        }
            .Select(context =>
                context.HasFlag(Microsoft.CodeAnalysis.NullableContext.AnnotationsEnabled)
            )
            .ToArray();

        Assert.Equal([false, true], contexts);
        Assert.Equal(
            Microsoft.CodeAnalysis.NullableContextOptions.Enable,
            library.Compilation.Options.NullableContextOptions
        );
        Assert.Equal(
            Microsoft.CodeAnalysis.NullableContextOptions.Disable,
            legacy.Compilation.Options.NullableContextOptions
        );
        Assert.Equal(
            (int)Microsoft.CodeAnalysis.NullableContextOptions.Disable,
            legacy.Snapshot.NullableContextOptions
        );
        Assert.Equal(
            (int)Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp12,
            legacy.Snapshot.LanguageVersion
        );
        Assert.Equal(
            Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp12,
            ((Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)source.Tree.Options).LanguageVersion
        );
        Assert.Equal(
            (int)Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp14,
            library.Snapshot.LanguageVersion
        );
    }

    [Fact]
    public async Task Output_matches_the_compact_cli_format_including_fix_markers()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new("B.cs", "class B\n{\n    string Value => string.Empty;\n}\n"),
                new("A.cs", "class A\n{\n    string Value => nameof(string.Empty);\n}\n"),
            ]
        );
        var empty = CodeType.Of<string>().Member(nameof(string.Empty));
        var rules = new RuleCatalog();
        rules
            .Rule("EMPTY", "Use \"\" instead of string.Empty.")
            .For(empty.References)
            .Forbid(fix: reference =>
                Fix.For(reference)
                    .ReplaceWithLiteral("")
                    .SafeWhen(change => change.Before.RefersTo(empty) && change.After.Is(""))
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            EMPTY Use "" instead of string.Empty.
            A.cs
              3:28
            B.cs
              +3:21

            """.Replace("\r\n", "\n"),
            result.Output
        );
    }

    [Fact]
    public void Implicit_usings_match_the_dotnet_sdk_defaults_without_reportable_source()
    {
        var workspace = fixture.Workspace();
        const string source = """
            class Reader
            {
                async Task<int> Count(List<string> values, CancellationToken cancellation)
                {
                    using var client = new HttpClient();
                    await File.WriteAllTextAsync(Path.GetTempFileName(), "", cancellation);
                    return values.Count(value => value.Length > 0) + Console.WindowWidth;
                }
            }
            """;

        var implicitUsings = workspace.AddProject(
            "Implicit",
            [new("Reader.cs", source)],
            implicitUsings: true
        );
        var explicitUsings = workspace.AddProject(
            "Explicit",
            [new("Reader.cs", source)],
            allowErrors: true
        );

        Assert.Equal(
            [("Reader.cs", false), ("Implicit.GlobalUsings.g.cs", true)],
            implicitUsings.Snapshot.Documents.Select(document =>
                (document.Path, document.IsGenerated)
            )
        );
        Assert.Equal(
            """
            // <auto-generated/>
            global using global::System;
            global using global::System.Collections.Generic;
            global using global::System.IO;
            global using global::System.Linq;
            global using global::System.Net.Http;
            global using global::System.Threading;
            global using global::System.Threading.Tasks;

            """,
            implicitUsings.Snapshot.Documents[1].Text
        );
        Assert.Contains(
            explicitUsings.Compilation.GetDiagnostics(TestContext.Current.CancellationToken),
            diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
        );
    }

    [Fact]
    public void Per_project_language_options_control_available_syntax()
    {
        var workspace = fixture.Workspace();
        const string source = "class A { int[] Values = []; }";
        var old = workspace.AddProject(
            "Old",
            [new("Old.cs", source)],
            languageVersion: Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp11,
            allowErrors: true
        );
        var modern = workspace.AddProject(
            "Modern",
            [new("Modern.cs", source)],
            languageVersion: Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp12
        );

        var errors = new[] { old, modern }
            .Select(project =>
                project
                    .Compilation.GetDiagnostics()
                    .Any(diagnostic =>
                        diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
                    )
            )
            .ToArray();

        Assert.Equal([true, false], errors);
    }

    [Fact]
    public async Task Generated_custom_candidates_never_report_and_linked_frameworks_withhold_inactive_fixes()
    {
        var workspace = fixture.Workspace();
        var source = new TestSource(
            "Shared.cs",
            """
            class Shared
            {
            #if FEATURE
                string M() => string.Empty;
            #endif
            }
            """
        );
        workspace.AddProject(
            "Library",
            [source, new("Generated.g.cs", "class Generated { }", true)],
            framework: "net9.0",
            symbols: ["FEATURE"]
        );
        workspace.AddProject("Library", [source], framework: "net10.0");
        var rules = new RuleCatalog();
        var allFiles = CodeQuery<CodeFile>.Create(solution =>
            solution
                .Projects.SelectMany(project => project.Sources)
                .Select(source => new CodeFile(source))
        );
        rules
            .Rule("GENERATED", "Do not report generated files.")
            .For(allFiles.Where(file => file.Source.Document.IsGenerated))
            .Forbid();
        rules
            .Rule("EMPTY", "Use a literal.")
            .For(CodeType.Of<string>().Member(nameof(string.Empty)).References)
            .Forbid(fix: reference =>
                SourceChanges.Propose(
                    [SourceChanges.Replace(reference.Source!, reference.Syntax!.Span, "\"\"")],
                    _ => true
                )
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            [new TestFinding("EMPTY", "Shared.cs", 4, 19, "string.Empty", false)],
            result.Findings
        );
        Assert.Equal(source.Text, result.FixedText("Shared.cs"));
    }

    [Fact]
    public async Task Conflicting_fixes_are_withheld_as_complete_batches()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "class A { int M() => 1; }")]);
        var rules = new RuleCatalog();
        var literals = Code.Nodes<LiteralExpressionSyntax>();
        rules
            .Rule("FIRST", "Use two.")
            .For(literals)
            .Forbid(fix: node =>
                SourceChanges.Propose(
                    [SourceChanges.Replace(node.Source, node.Syntax.Span, "2")],
                    _ => true
                )
            );
        rules
            .Rule("SECOND", "Use three.")
            .For(literals)
            .Forbid(fix: node =>
                SourceChanges.Propose(
                    [SourceChanges.Replace(node.Source, node.Syntax.Span, "3")],
                    _ => true
                )
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new TestFinding("FIRST", "A.cs", 1, 22, "1", false),
                new TestFinding("SECOND", "A.cs", 1, 22, "1", false),
            ],
            result.Findings
        );
        Assert.Equal("class A { int M() => 1; }", result.FixedText("A.cs"));
    }

    [Fact]
    public void Workspace_projects_replace_referenced_assemblies_of_the_same_name()
    {
        var workspace = fixture.Workspace();
        var manifest = workspace.AddProject(
            "DrillPress.Manifest",
            [new("Thing.cs", "namespace Fake; public class Thing { }")]
        );

        var app = workspace.AddProject(
            "App",
            [new("App.cs", "class App { Fake.Thing? Value; }")],
            dependencies: [manifest]
        );

        Assert.Empty(
            app.Compilation.GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(diagnostic =>
                    diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
                )
        );
        Assert.DoesNotContain(
            manifest.Compilation.ReferencedAssemblyNames,
            identity => identity.Name == "DrillPress.Manifest"
        );
    }

    [Fact]
    public async Task Console_projects_compile_top_level_statements()
    {
        var workspace = fixture.Workspace();
        var tool = workspace.AddProject(
            "Tool",
            [new("Program.cs", "System.Console.WriteLine(1);")],
            outputKind: Microsoft.CodeAnalysis.OutputKind.ConsoleApplication
        );
        var rules = new RuleCatalog();
        rules.Rule("CONSOLE", "Log instead.").For(Code.Calls.ToMethodsNamed("WriteLine")).Forbid();

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            CONSOLE Log instead.
            Program.cs
              1:16

            """.ReplaceLineEndings("\n"),
            result.Output
        );
        Assert.Equal(
            (int)Microsoft.CodeAnalysis.OutputKind.ConsoleApplication,
            tool.Snapshot.OutputKind
        );
    }
}
