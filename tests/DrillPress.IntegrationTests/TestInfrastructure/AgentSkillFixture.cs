using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.TestInfrastructure;

public sealed class AgentSkillFixture : IntegrationTest
{
    private static readonly CSharpParseOptions _parseOptions = new(LanguageVersion.CSharp14);

    private readonly MetadataReference[] _references = new RuleTestWorkspace()
        .AddProject("References", [new("References.cs", "class ReferenceAnchor { }")])
        .Compilation.References.ToArray();

    private static string SkillDirectory { get; } = RepositoryPath("skills", "drillpress-rules");

    public static TheoryData<string, string> ReadExamples()
    {
        var examples = new TheoryData<string, string>();
        foreach (var document in Documents())
        {
            var blocks = Regex.Matches(
                FileSystem.File.ReadAllText(document).ReplaceLineEndings("\n"),
                "```csharp\n([\\s\\S]*?)```"
            );
            for (var index = 0; index < blocks.Count; index++)
            {
                examples.Add($"{Relative(document)}#{index + 1}", blocks[index].Groups[1].Value);
            }
        }
        return examples;
    }

    /// <summary>Compiles a block with the bundled example rules, then runs its parameterless [Fact] methods.</summary>
    public async Task<string[]> CheckAsync(string name, string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code, _parseOptions, name);
        var isProgram = tree.GetCompilationUnitRoot().Members.OfType<GlobalStatementSyntax>().Any();
        var compilation = CSharpCompilation.Create(
            "SkillExample" + Guid.NewGuid().ToString("N"),
            [
                CSharpSyntaxTree.ParseText(
                    """
                    global using System;
                    global using System.Collections.Generic;
                    global using System.IO;
                    global using System.Linq;
                    global using System.Threading;
                    global using System.Threading.Tasks;
                    """,
                    _parseOptions
                ),
                CSharpSyntaxTree.ParseText(
                    FileSystem.File.ReadAllText(
                        FileSystem.Path.Combine(SkillDirectory, "examples", "ExampleRules.cs")
                    ),
                    _parseOptions
                ),
                tree,
            ],
            _references,
            new CSharpCompilationOptions(
                isProgram ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );
        using var image = new MemoryStream();
        var emitted = compilation.Emit(
            image,
            cancellationToken: TestContext.Current.CancellationToken
        );
        if (!emitted.Success)
            return emitted
                .Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.ToString())
                .ToArray();
        return await RunFactsAsync(Assembly.Load(image.ToArray()));
    }

    public string[] BrokenLinks() =>
        Documents()
            .SelectMany(document =>
                Regex
                    .Matches(FileSystem.File.ReadAllText(document), @"\]\(([^)#]+)(?:#[^)]*)?\)")
                    .Select(match => match.Groups[1].Value)
                    .Where(link => !Uri.TryCreate(link, UriKind.Absolute, out _))
                    .Where(link =>
                        !FileSystem.File.Exists(
                            FileSystem.Path.Combine(
                                FileSystem.Path.GetDirectoryName(document)!,
                                link
                            )
                        )
                    )
                    .Select(link => $"{Relative(document)} -> {link}")
            )
            .ToArray();

    public (string Name, string Description) Metadata()
    {
        var text = FileSystem
            .File.ReadAllText(FileSystem.Path.Combine(SkillDirectory, "SKILL.md"))
            .ReplaceLineEndings("\n");
        var frontmatter = Regex.Match(text, "^---\n([\\s\\S]*?)\n---\n").Groups[1].Value;
        return (
            Regex.Match(frontmatter, "^name: (.+)$", RegexOptions.Multiline).Groups[1].Value,
            Regex.Match(frontmatter, "^description: (.+)$", RegexOptions.Multiline).Groups[1].Value
        );
    }

    private static async Task<string[]> RunFactsAsync(Assembly assembly)
    {
        var failures = new List<string>();
        foreach (
            var method in assembly
                .GetExportedTypes()
                .SelectMany(type => type.GetMethods())
                .Where(method => method.GetCustomAttribute<FactAttribute>() is not null)
        )
        {
            try
            {
                var instance = Activator.CreateInstance(method.DeclaringType!);
                if (method.Invoke(instance, null) is Task task)
                    await task;
            }
            catch (Exception exception)
            {
                var cause = exception is TargetInvocationException { InnerException: { } inner }
                    ? inner
                    : exception;
                failures.Add($"{method.DeclaringType!.Name}.{method.Name}: {cause.Message}");
            }
        }
        return failures.ToArray();
    }

    private static IEnumerable<string> Documents() =>
        FileSystem
            .Directory.EnumerateFiles(SkillDirectory, "*.md", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal);

    private static string Relative(string path) =>
        FileSystem.Path.GetRelativePath(SkillDirectory, path).Replace('\\', '/');
}
