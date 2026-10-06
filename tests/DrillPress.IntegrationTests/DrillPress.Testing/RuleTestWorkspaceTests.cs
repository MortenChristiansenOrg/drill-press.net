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
        var rules = new RuleSet();
        var allFiles = CodeQuery<CodeFile>.Create(solution =>
            solution
                .Projects.SelectMany(project => project.Sources)
                .Select(source => new CodeFile(source))
        );
        rules
            .For(allFiles.Where(file => file.Source.Document.IsGenerated))
            .Forbid("GENERATED", "Do not report generated files.");
        rules
            .For(Code.MemberReferences.Where(Members.Are<string>(nameof(string.Empty))))
            .Forbid(
                "EMPTY",
                "Use a literal.",
                fix: reference =>
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
        var rules = new RuleSet();
        var literals = Sources.Nodes<LiteralExpressionSyntax>();
        rules
            .For(literals)
            .Forbid(
                "FIRST",
                "Use two.",
                fix: node =>
                    SourceChanges.Propose(
                        [SourceChanges.Replace(node.Source, node.Syntax.Span, "2")],
                        _ => true
                    )
            );
        rules
            .For(literals)
            .Forbid(
                "SECOND",
                "Use three.",
                fix: node =>
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
}
